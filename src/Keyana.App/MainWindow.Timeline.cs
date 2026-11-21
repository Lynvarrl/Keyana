using Keyana.App.Controls;
using Keyana.Storage;

namespace Keyana.App;

/// <summary>
/// 时段分布条的粒度：每块几分钟。
///
/// 数据层只有一档精度（分钟），粒度是纯展示选择——所以换粒度不重新查库，
/// 只是把同一份分钟序列按不同大小重新聚合；只有从「十分钟」切到更细的档位时，
/// 才需要额外取一次分钟级数据（十分钟汇总表里没有这种精度）。
/// </summary>
public partial class MainWindow
{
    private const long SecondsPerDay = 24 * 60 * 60;

    private int _timelineBucketMinutes = TimeOfDayTimelineControl.DefaultBucketMinutes;
    private ulong[]? _timelineMinuteTotals;
    private long? _timelineMinuteStartUtc;
    private DateTimeOffset _timelineMinuteLoadedAt = DateTimeOffset.MinValue;
    private bool _timelineMinutesLoading;
    private DateTimeOffset _timelineMinuteLastAttempt = DateTimeOffset.MinValue;

    private void InitializeTimelineControls()
    {
        _timelineBucketMinutes = TimeOfDayTimelineControl.DefaultBucketMinutes;
        MinuteBucketPicker.Value = _timelineBucketMinutes;
        MinuteBucketPicker.ValueChanged += OnTimelineBucketChanged;
        UpdateTimelineCaption();
        ApplyTimelineNow();
    }

    private async void OnTimelineBucketChanged(object? sender, EventArgs eventArgs)
    {
        _timelineBucketMinutes = MinuteBucketPicker.Value;
        UpdateTimelineCaption();

        // 应用设置时也是走 Value 的 setter：别把「设置刚被应用」当成用户改了一次，
        // 否则每次启动都会白写一遍设置文件。
        if (_settingsUiUpdating)
        {
            return;
        }

        if (_settings is not null)
        {
            _settings.TimelineBucketMinutes = _timelineBucketMinutes;
            _ = SaveSettingsAsync();
        }

        if (NeedsTimelineMinuteData)
        {
            await EnsureTimelineMinutesAsync(force: true);
        }

        ApplyTimelineNow();
    }

    /// <summary>标题下方的说明：写清楚每块多长，也写清楚跨天是累加。</summary>
    private void UpdateTimelineCaption()
    {
        var sameDay = _selectedRange is { } range && range.EndUtc - range.StartUtc <= SecondsPerDay;
        var scope = sameDay ? "所选范围按每天相同的时段汇总" : "所选范围按每天相同的时段累加（多日叠加）";
        TimelineCaptionText.Text = $"{scope}，每块 {_timelineBucketMinutes} 分钟";
    }

    /// <summary>
    /// 这一档能不能由十分钟汇总直接拼出来：10 / 30 / 60 能，其余要分钟级数据。
    /// 15 虽然比十分钟粗，但不是 10 的整数倍，十分钟汇总拼不出来——别按「粗细」判断。
    /// </summary>
    private bool NeedsTimelineMinuteData =>
        _timelineBucketMinutes % TimeOfDayTimelineControl.DefaultBucketMinutes != 0;

    /// <summary>
    /// 按当前粒度重画。分钟级数据没到位（或范围换了）时先按十分钟一档画着，
    /// 不要让条带空着——数据一到会自动重画。
    /// </summary>
    private void ApplyTimelineNow()
    {
        if (NeedsTimelineMinuteData &&
            _timelineMinuteTotals is not null &&
            _selectedRange is { } range &&
            _timelineMinuteStartUtc == range.StartUtc)
        {
            var minutes = (ulong[])_timelineMinuteTotals!.Clone();
            AddLiveMinuteToTimeline(minutes);
            DailyTimeline.SetData(minutes, 1, _timelineBucketMinutes, _currentPalette);
            return;
        }

        DailyTimeline.SetData(
            _selectedDisplayTimeCounts,
            10,
            NeedsTimelineMinuteData ? TimeOfDayTimelineControl.DefaultBucketMinutes : _timelineBucketMinutes,
            _currentPalette);
    }

    /// <summary>
    /// 分钟级数据是否该重取。单日范围只有 1440 行，一分钟的保鲜期；跨多日的范围
    /// （「全部」可能十几万行）放宽到五分钟，避免拖累界面。
    /// </summary>
    private bool TimelineMinutesStale()
    {
        if (_timelineMinuteTotals is null || _selectedRange is not { } range)
        {
            return true;
        }

        if (_timelineMinuteStartUtc != range.StartUtc)
        {
            return true;
        }

        var span = range.EndUtc - range.StartUtc;
        var maxAge = span <= SecondsPerDay ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(5);
        return DateTimeOffset.UtcNow - _timelineMinuteLoadedAt > maxAge;
    }

    /// <summary>
    /// 取分钟级时段数据（1440 项，按一天中的位置累加）。
    /// 与十分钟那条路径一致：当前这一分钟还没落盘，查询时排除它，再把实时计数单独加回去。
    /// </summary>
    private async Task EnsureTimelineMinutesAsync(bool force = false)
    {
        if (_repository is null || _selectedRange is not { } range || _timelineMinutesLoading)
        {
            return;
        }

        if (!force && !TimelineMinutesStale())
        {
            return;
        }

        // 查失败时不要每次实时刷新都重试：界面每 150ms 就可能问一次。
        if (!force && DateTimeOffset.UtcNow - _timelineMinuteLastAttempt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _timelineMinuteLastAttempt = DateTimeOffset.UtcNow;
        _timelineMinutesLoading = true;
        try
        {
            var live = _recorder?.GetCurrentSnapshot();
            ulong[] totals;
            if (live is not null && live.BucketStartUtc >= range.StartUtc && live.BucketStartUtc < range.EndUtc)
            {
                totals = await _repository.QueryTimeOfDayMinuteTotalsAsync(
                    range.StartUtc, live.BucketStartUtc, _lifetimeCancellation.Token);
                var afterwards = await _repository.QueryTimeOfDayMinuteTotalsAsync(
                    live.BucketStartUtc + TimeBuckets.MinuteSeconds, range.EndUtc, _lifetimeCancellation.Token);
                AddCounts(totals, afterwards);
            }
            else
            {
                totals = await _repository.QueryTimeOfDayMinuteTotalsAsync(
                    range.StartUtc, range.EndUtc, _lifetimeCancellation.Token);
            }

            _timelineMinuteTotals = totals;
            _timelineMinuteStartUtc = range.StartUtc;
            _timelineMinuteLoadedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                ErrorLog.Record("Timeline.Minutes", exception);
            }
        }
        finally
        {
            _timelineMinutesLoading = false;
        }

        ApplyTimelineNow();
    }

    /// <summary>把当前这一分钟的实时计数补进分钟序列。</summary>
    private void AddLiveMinuteToTimeline(ulong[] minutes)
    {
        if (_recorder?.GetCurrentSnapshot() is not { } live)
        {
            return;
        }

        if (_selectedRange is not { } range ||
            live.BucketStartUtc < range.StartUtc ||
            live.BucketStartUtc >= range.EndUtc)
        {
            return;
        }

        var local = DateTimeOffset.FromUnixTimeSeconds(live.BucketStartUtc).ToLocalTime();
        var index = (local.Hour * 60) + local.Minute;
        minutes[index] = checked(minutes[index] + (ulong)live.TotalCount);
    }
}
