namespace Keyana.Storage;

/// <summary>
/// 一个区间内累计的滚轮原始 delta（一格 = 120）。
/// </summary>
/// <param name="Up">向上滚动的累计 delta。</param>
/// <param name="Down">向下滚动的累计 delta。</param>
public readonly record struct WheelDistanceTotals(long Up, long Down)
{
    public long Total => Up + Down;

    public static WheelDistanceTotals operator +(WheelDistanceTotals left, WheelDistanceTotals right) =>
        new(left.Up + right.Up, left.Down + right.Down);
}
