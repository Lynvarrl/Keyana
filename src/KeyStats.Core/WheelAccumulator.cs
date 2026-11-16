namespace KeyStats.Core;

/// <summary>
/// 把连续的滚轮增量累计成「格」。
///
/// 高精度滚轮（以及部分触摸板）一次只发几十个单位，如果按每条消息计一次，
/// 轻轻一滑就会变成几十次。Windows 的标准刻度是 120，这里保留余数，
/// 只有凑满一格才返回一次计数；方向由返回值的正负表示。
///
/// 这个类型只做纯计算，方便脱离 Raw Input 单独测试。
/// </summary>
public sealed class WheelAccumulator
{
    public const int NotchDelta = 120;

    private int _remainder;

    /// <summary>加入一次原始增量，返回本次累计出的格数（正数向上、负数向下）。</summary>
    public int Add(int delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        var total = _remainder + delta;
        var notches = total / NotchDelta;
        _remainder = total - (notches * NotchDelta);
        return notches;
    }

    public void Reset() => _remainder = 0;
}
