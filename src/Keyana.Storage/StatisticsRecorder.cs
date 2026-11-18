using System.Threading.Channels;
using Keyana.Core;

namespace Keyana.Storage;

/// <summary>
/// 把内存中的每分钟计数落到 SQLite。
///
/// 生命周期只有三态，全部在 <see cref="_gate"/> 内迁移：
/// <c>Accepting →（DisposeAsync）Draining → Completed</c>。进入 Draining 后
/// <see cref="RecordKeyPress"/> 一律拒绝，避免出现「界面计了、库里永远不会写」的
/// 退出边界丢数。<see cref="DisposeAsync"/> 本身是幂等的，重复调用等待同一个收尾任务。
///
/// 保存模型（针对旧实现的三类丢数路径）：
/// <list type="bullet">
/// <item>每个分钟桶同时最多只有一份待写快照，按桶合并为最新 revision，队列天然有界，
/// 不会像旧实现那样把同一分钟的多个快照排队后由旧快照后写、把新数据覆盖回去；</item>
/// <item>写成功才从待写表摘除；失败保留最新快照并退避重试，退出前再兜底重试，
/// 不再出现「内存已清空、失败即永久丢数」；</item>
/// <item>单写线程串行提交，并用每桶单调 revision 校验，保证同一桶的写入不倒退。</item>
/// </list>
/// </summary>
public sealed class StatisticsRecorder : IAsyncDisposable
{
    /// <summary>单次排空里每条快照的最大尝试次数；仍失败就留到下一次定时/手动刷新。</summary>
    private const int AttemptsPerDrain = 3;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SecondRetryDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>退出时等待保存线程的上限：磁盘卡死也不能让窗口永远关不掉。</summary>
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(10);

    private readonly IMinuteBucketStore _store;
    private readonly IUtcClock _clock;
    private readonly TimeSpan _flushInterval;
    private readonly object _gate = new();
    private readonly object _disposeGate = new();
    private readonly Channel<byte> _workSignal;
    private readonly CancellationTokenSource _timerCancellation = new();
    private readonly Dictionary<long, PendingBucket> _pending = [];
    private readonly Dictionary<long, long> _writtenRevision = [];
    private readonly Dictionary<long, List<FlushWaiter>> _waiters = [];
    private readonly Task _saveWorker;
    private readonly Task _timerWorker;
    private long _currentMinuteStart;
    private uint[] _currentCounts;
    private long _revisionSeed;
    private long _lastSavedUnixSeconds;
    private string? _lastError;
    private LifecycleState _state = LifecycleState.Accepting;
    private Task? _disposeTask;

    private StatisticsRecorder(
        IMinuteBucketStore store,
        IUtcClock clock,
        TimeSpan flushInterval,
        MinuteBucketSnapshot initialBucket)
    {
        _store = store;
        _clock = clock;
        _flushInterval = flushInterval;
        _currentMinuteStart = initialBucket.BucketStartUtc;
        _currentCounts = (uint[])initialBucket.Counts.Clone();

        // 容量 1 + DropWrite：并发触发时只保留一个「有活干」的信号，不堆积。
        _workSignal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _saveWorker = Task.Run(ProcessSaveQueueAsync);
        _timerWorker = Task.Run(PeriodicFlushAsync);
    }

    public DateTimeOffset? LastSavedUtc
    {
        get
        {
            var value = Interlocked.Read(ref _lastSavedUnixSeconds);
            return value == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value);
        }
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public static async Task<StatisticsRecorder> CreateAsync(
        IMinuteBucketStore store,
        TimeSpan flushInterval,
        IUtcClock? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (flushInterval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(flushInterval));
        }

        clock ??= SystemUtcClock.Instance;
        var currentMinuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
        var initialBucket = await store.GetMinuteBucketAsync(currentMinuteStart, cancellationToken)
            ?? new MinuteBucketSnapshot(currentMinuteStart, new uint[KeyCatalog.Count]);
        return new StatisticsRecorder(store, clock, flushInterval, initialBucket);
    }

    /// <summary>
    /// 记录一次按键。返回 false 表示录制器已进入退出流程，本次输入未被接受
    /// （调用方不要把它算进「已保存」的预期里）。
    /// </summary>
    public bool RecordKeyPress(KeyId keyId)
    {
        var staged = false;
        lock (_gate)
        {
            if (_state != LifecycleState.Accepting)
            {
                return false;
            }

            var minuteStart = TimeBuckets.ToMinuteStart(_clock.UtcNow);

            // 只在时间前进时切桶。时钟被回拨时继续累计当前桶：若按旧实现切到过去的
            // 分钟，会用「绝对覆盖」把已经写进历史的那一分钟改小。
            if (minuteStart > _currentMinuteStart)
            {
                staged = TryStageCurrentLocked();
                _currentMinuteStart = minuteStart;
                _currentCounts = new uint[KeyCatalog.Count];
            }

            var keyIndex = (int)keyId;
            if (_currentCounts[keyIndex] < uint.MaxValue)
            {
                _currentCounts[keyIndex]++;
            }
        }

        if (staged)
        {
            SignalWorker();
        }

        return true;
    }

    public MinuteBucketSnapshot GetCurrentSnapshot()
    {
        lock (_gate)
        {
            return new MinuteBucketSnapshot(_currentMinuteStart, (uint[])_currentCounts.Clone());
        }
    }

    /// <summary>
    /// 把当前分钟的绝对计数写入存储，并等到「至少这个 revision」落盘为止。
    /// 保存失败会先做有限重试，仍失败则抛出，由界面提示；数据仍留在待写表里，
    /// 后续定时刷新或下一次手动保存会继续重试，不会因为一次失败永久丢数。
    /// </summary>
    public async Task FlushNowAsync(CancellationToken cancellationToken = default)
    {
        Task? waitTask = null;
        lock (_gate)
        {
            if (_state != LifecycleState.Accepting)
            {
                throw new ObjectDisposedException(nameof(StatisticsRecorder));
            }

            if (TryStageCurrentLocked(out var bucketStart, out var revision))
            {
                var waiter = new FlushWaiter(
                    revision,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                if (!_waiters.TryGetValue(bucketStart, out var list))
                {
                    list = [];
                    _waiters[bucketStart] = list;
                }

                list.Add(waiter);
                waitTask = waiter.Completion.Task;
            }
        }

        if (waitTask is null)
        {
            return;
        }

        SignalWorker();
        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        lock (_gate)
        {
            if (_state != LifecycleState.Accepting)
            {
                return;
            }

            // 先关门再取快照：此后 RecordKeyPress 一律返回 false，不会再有「接受了
            // 却进不了这次 flush」的输入。
            _state = LifecycleState.Draining;
            TryStageCurrentLocked();
        }

        _timerCancellation.Cancel();
        try
        {
            await _timerWorker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // 让保存线程排空待写表；TryComplete 之后它会做完最后一次兜底重试再退出。
        SignalWorker();
        _workSignal.Writer.TryComplete();

        try
        {
            await _saveWorker.WaitAsync(ShutdownFlushTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Volatile.Write(ref _lastError, "退出时保存超时，最后一次写入可能未完成。");
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
        }

        lock (_gate)
        {
            _state = LifecycleState.Completed;
            // 正常情况下不会再有等待者；这里兜底，避免调用方永久挂住。
            foreach (var list in _waiters.Values)
            {
                foreach (var waiter in list)
                {
                    waiter.Completion.TrySetException(
                        new ObjectDisposedException(nameof(StatisticsRecorder)));
                }
            }

            _waiters.Clear();
        }

        _timerCancellation.Dispose();
    }

    private async Task PeriodicFlushAsync()
    {
        using var timer = new PeriodicTimer(_flushInterval);
        while (await timer.WaitForNextTickAsync(_timerCancellation.Token).ConfigureAwait(false))
        {
            lock (_gate)
            {
                if (_state != LifecycleState.Accepting || !TryStageCurrentLocked())
                {
                    continue;
                }
            }

            SignalWorker();
        }
    }

    private async Task ProcessSaveQueueAsync()
    {
        await foreach (var _ in _workSignal.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await DrainPendingAsync(isShutdown: false).ConfigureAwait(false);
        }

        // 通道关闭 = 正在退出：此时不再等下一次定时器，直接做最后一轮兜底重试。
        await DrainPendingAsync(isShutdown: true).ConfigureAwait(false);
    }

    private async Task DrainPendingAsync(bool isShutdown)
    {
        while (true)
        {
            long bucketStart;
            PendingBucket bucket;
            lock (_gate)
            {
                if (!TryPeekNextPendingLocked(out bucketStart, out bucket))
                {
                    return;
                }
            }

            var snapshot = new MinuteBucketSnapshot(bucketStart, bucket.Counts);
            Exception? failure = null;
            for (var attempt = 1; attempt <= AttemptsPerDrain; attempt++)
            {
                try
                {
                    await _store.UpsertMinuteBucketAsync(snapshot).ConfigureAwait(false);
                    failure = null;
                    break;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    if (attempt < AttemptsPerDrain)
                    {
                        await Task.Delay(attempt == 1 ? FirstRetryDelay : SecondRetryDelay)
                            .ConfigureAwait(false);
                    }
                }
            }

            if (failure is null)
            {
                CompleteWrite(bucketStart, bucket.Revision);
                continue;
            }

            Volatile.Write(ref _lastError, failure.Message);
            lock (_gate)
            {
                FaultWaitersLocked(bucketStart, failure);
            }

            // 待写快照保留在表里：定时刷新、手动保存或退出兜底都会再来一次。
            // 退出阶段已无后续机会，直接结束，让 Dispose 在超时前收尾。
            return;
        }
    }

    private void CompleteWrite(long bucketStart, long revision)
    {
        lock (_gate)
        {
            if (!_writtenRevision.TryGetValue(bucketStart, out var written) || revision > written)
            {
                _writtenRevision[bucketStart] = revision;
            }

            if (_pending.TryGetValue(bucketStart, out var latest) && latest.Revision == revision)
            {
                // 没有更新的待写快照：连同 revision 记录一起清掉，字典不会随运行时长增长。
                _pending.Remove(bucketStart);
                _writtenRevision.Remove(bucketStart);
            }

            Volatile.Write(ref _lastSavedUnixSeconds, _clock.UtcNow.ToUnixTimeSeconds());
            Volatile.Write(ref _lastError, null);
            CompleteWaitersLocked(bucketStart, revision);
        }
    }

    /// <summary>
    /// 把当前分钟的计数放进待写表。内容与已有待写快照完全一致时不重复入队，
    /// 只返回已有 revision，让调用方等待那次写入即可。
    /// </summary>
    private bool TryStageCurrentLocked()
    {
        return TryStageCurrentLocked(out _, out _);
    }

    private bool TryStageCurrentLocked(out long bucketStart, out long revision)
    {
        bucketStart = _currentMinuteStart;
        revision = 0;

        if (CountTotal(_currentCounts) == 0)
        {
            return false;
        }

        if (_pending.TryGetValue(bucketStart, out var existing) &&
            existing.Counts.AsSpan().SequenceEqual(_currentCounts))
        {
            revision = existing.Revision;
            return true;
        }

        revision = ++_revisionSeed;
        _pending[bucketStart] = new PendingBucket((uint[])_currentCounts.Clone(), revision);
        return true;
    }

    private bool TryPeekNextPendingLocked(out long bucketStart, out PendingBucket bucket)
    {
        bucketStart = 0;
        bucket = null!;
        var found = false;
        foreach (var pair in _pending)
        {
            if (found && pair.Key >= bucketStart)
            {
                continue;
            }

            bucketStart = pair.Key;
            bucket = pair.Value;
            found = true;
        }

        return found;
    }

    private void CompleteWaitersLocked(long bucketStart, long writtenRevision)
    {
        if (!_waiters.TryGetValue(bucketStart, out var list))
        {
            return;
        }

        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (list[index].Revision > writtenRevision)
            {
                continue;
            }

            list[index].Completion.TrySetResult();
            list.RemoveAt(index);
        }

        if (list.Count == 0)
        {
            _waiters.Remove(bucketStart);
        }
    }

    private void FaultWaitersLocked(long bucketStart, Exception exception)
    {
        if (!_waiters.Remove(bucketStart, out var list))
        {
            return;
        }

        foreach (var waiter in list)
        {
            waiter.Completion.TrySetException(exception);
        }
    }

    private void SignalWorker() => _workSignal.Writer.TryWrite(0);

    private static long CountTotal(ReadOnlySpan<uint> counts)
    {
        long total = 0;
        foreach (var count in counts)
        {
            total += count;
        }

        return total;
    }

    private enum LifecycleState
    {
        Accepting,
        Draining,
        Completed,
    }

    private sealed class PendingBucket(uint[] counts, long revision)
    {
        public uint[] Counts { get; } = counts;

        public long Revision { get; } = revision;
    }

    private sealed record FlushWaiter(long Revision, TaskCompletionSource Completion);
}
