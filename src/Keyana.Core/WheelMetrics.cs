namespace Keyana.Core;

/// <summary>
/// 滚轮双指标的换算与分类。距离存原始 delta（一格 = 120 单位），
/// 换成「格」「厘米」只发生在展示层，绝不写回存储。
/// </summary>
public static class WheelMetrics
{
    /// <summary>Windows 标准刻度：一格 = 120。</summary>
    public const int NotchDelta = 120;

    /// <summary>
    /// 一格滚动折算的逻辑像素。系统默认「每次滚 3 行」、每行 20 逻辑像素，
    /// 于是 1 格 ≈ 60 px。这是估算，真实值跟系统滚动行数、应用自己的滚动实现都有关。
    /// </summary>
    public const double PixelsPerNotch = 60d;

    /// <summary>WPF 的逻辑像素基准 DPI。</summary>
    public const double ReferenceDpi = 96d;

    /// <summary>承载「滚动距离」的旧键位：不再是次数，只作为历史距离的落脚点。</summary>
    public static bool IsDistanceKey(KeyId id) => id is KeyId.MouseWheelUp or KeyId.MouseWheelDown;

    /// <summary>承载「手势动作数」的新键位。</summary>
    public static bool IsGestureKey(KeyId id) => id is KeyId.WheelGestureUp or KeyId.WheelGestureDown;

    /// <summary>取原始增量的绝对值，不用 Math.Abs(long) 以避开 long.MinValue 溢出。</summary>
    public static long Magnitude(int delta) => delta < 0 ? -(long)delta : delta;

    public static double Notches(long distance) => distance / (double)NotchDelta;

    /// <summary>
    /// 把累计 delta 估算成厘米。
    ///
    /// 换算只用 DIP（设备无关像素）与 96 DPI 基准：Windows 的缩放比例本来就是为了让
    /// 同一个 DIP 在任何屏幕上都是同样的物理尺寸，所以系统 DPI 在这里天然抵消，
    /// 再乘一次或除一次都是重复计算。<paramref name="scale"/> 是用户自己的修正系数，
    /// 用来补偿「系统每次滚 5 行」这类与默认 3 行不同的设置。
    /// </summary>
    /// <param name="distance">累计原始 delta。</param>
    /// <param name="scale">用户修正系数，1 表示按「一格 3 行、每行 20 DIP」估算；非正数按 1 处理。</param>
    public static double EstimateCentimeters(long distance, double scale)
    {
        if (distance <= 0)
        {
            return 0d;
        }

        var factor = double.IsFinite(scale) && scale > 0 ? scale : 1d;
        return Notches(distance) * PixelsPerNotch / factor / ReferenceDpi * 2.54d;
    }
}
