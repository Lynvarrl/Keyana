namespace Keyana.Core;

/// <summary>滚轮方向。以上滑（delta &gt; 0）为正，与 <see cref="KeyId.WheelGestureUp"/> 对应。</summary>
public enum WheelDirection
{
    Up,
    Down,
}

/// <summary>
/// 一次滚轮原始事件。这是「旁路」观测用的数据：无论是否计数、是否处于校准模式都会发出，
/// 校准面板靠它显示真实事件，主计数不依赖它。
/// </summary>
/// <param name="Delta">本条消息的原始增量，一格 = <see cref="WheelMetrics.NotchDelta"/>。</param>
/// <param name="Direction">由 delta 的符号决定。</param>
/// <param name="IsNewGesture">判定器是否把这条事件算作一次新的上滑 / 下滑动作。</param>
public readonly record struct WheelInputInfo(int Delta, WheelDirection Direction, bool IsNewGesture);

/// <summary>一次判定结果。<see cref="Accepted"/> 为 false 表示这条事件被忽略（delta 为 0）。</summary>
public readonly record struct WheelGestureResult(bool Accepted, WheelDirection Direction, bool IsNewGesture)
{
    public static readonly WheelGestureResult Ignored = new(false, WheelDirection.Up, false);
}

/// <summary>
/// 滚轮「手势动作数」判定：把一串原始滚动事件切成「几次上滑 / 下滑」。
///
/// 判定规则只有一条——距该方向**上一次滚动事件**超过窗口（毫秒）就算一次新手势，
/// 窗口内的后续事件并入当前手势。之所以用「上一次事件」而不是「本次手势的起点」：
/// 连续滚动时事件间隔远小于窗口，时间戳必须一路推进，否则滚动到一半就会被判成
/// 第二次手势，几百格的长滚动会变成十几次。
///
/// 方向分流：上滑与下滑各记各的时间戳。用一个全局时间戳的话，上→下→上来回切换
/// 会因为中间那次反向事件把计时刷新掉，三次真实动作被压成一次。
///
/// 时间用 <see cref="Environment.TickCount64"/>（单调、不受系统时钟跳变影响），
/// 由调用方传入，方便测试用固定刻度断言。
///
/// 本类型不做线程同步：Raw Input 回调、设备移除与校准开关都在同一个窗口消息线程上，
/// 加锁只会制造「多线程安全」的错觉。请勿跨线程调用。
/// </summary>
public sealed class WheelGestureDetector
{
    public const int MinWindowMs = 50;
    public const int MaxWindowMs = 500;
    public const int DefaultWindowMs = 200;

    private readonly Dictionary<nint, DeviceTimers> _devices = [];

    /// <summary>把窗口限制在可调范围内；越界一律拉回最近的合法值。</summary>
    public static int ClampWindow(int windowMs) => Math.Clamp(windowMs, MinWindowMs, MaxWindowMs);

    /// <summary>
    /// 判定一条滚轮事件。
    /// </summary>
    /// <param name="deviceHandle">设备句柄。每台设备独立计时，换设备不会续上一次的手势。</param>
    /// <param name="delta">原始增量，正数向上、负数向下；0 表示无效事件。</param>
    /// <param name="nowTicks">当前单调时刻，通常取 <see cref="Environment.TickCount64"/>。</param>
    /// <param name="windowMs">手势窗口，自动钳制到 50–500。</param>
    public WheelGestureResult Observe(nint deviceHandle, int delta, long nowTicks, int windowMs)
    {
        if (delta == 0)
        {
            // 无效事件不推进任何时间戳，否则一次噪声就能把进行中的手势截断。
            return WheelGestureResult.Ignored;
        }

        var direction = delta > 0 ? WheelDirection.Up : WheelDirection.Down;
        var window = ClampWindow(windowMs);

        if (!_devices.TryGetValue(deviceHandle, out var timers))
        {
            timers = new DeviceTimers();
            _devices[deviceHandle] = timers;
        }

        var lastTicks = direction == WheelDirection.Up ? timers.LastUpTicks : timers.LastDownTicks;
        var isNewGesture = lastTicks is not { } last || nowTicks - last > window;

        if (direction == WheelDirection.Up)
        {
            timers.LastUpTicks = nowTicks;
        }
        else
        {
            timers.LastDownTicks = nowTicks;
        }

        return new WheelGestureResult(true, direction, isNewGesture);
    }

    /// <summary>清空全部设备状态。暂停恢复、校准模式切换时调用，避免陈旧时间戳压掉第一下手势。</summary>
    public void Reset() => _devices.Clear();

    /// <summary>设备移除时只清该设备，其它设备正在进行的滚动不受影响。</summary>
    public void Reset(nint deviceHandle) => _devices.Remove(deviceHandle);

    private sealed class DeviceTimers
    {
        public long? LastUpTicks;
        public long? LastDownTicks;
    }
}
