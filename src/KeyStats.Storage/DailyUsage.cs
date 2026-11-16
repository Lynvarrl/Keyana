namespace KeyStats.Storage;

/// <summary>活跃日 / 火热日的判定阈值。口径是「本地自然日内全部输入次数」。</summary>
public static class DailyUsageThresholds
{
    /// <summary>达到这个输入量记为活跃日。</summary>
    public const ulong Active = 325;

    /// <summary>达到这个输入量记为火热日。</summary>
    public const ulong Fire = 7999;
}

/// <summary>
/// 单个本地自然日的使用情况。
/// <para>
/// <see cref="ActiveMinuteCount"/> 是「当天有输入的分钟数」，作为使用时长的口径：
/// 没有键鼠事件时无法判断用户是否还在电脑前，用它比用首末输入跨度更保守，也更稳定。
/// </para>
/// <para>
/// <see cref="HourCounts"/> 是 0～23 点的逐小时输入量，用来在每日活跃图里画出
/// 「这一天什么时候在用电脑」的分布，不影响任何指标口径。
/// </para>
/// </summary>
public sealed record DailyUsage(
    DateOnly Date,
    ulong TotalCount,
    int ActiveMinuteCount,
    IReadOnlyList<ulong> HourCounts)
{
    public bool IsActive => TotalCount >= DailyUsageThresholds.Active;

    public bool IsFire => TotalCount >= DailyUsageThresholds.Fire;

    /// <summary>最忙的一个小时；全天无输入时返回 -1。</summary>
    public int PeakHour
    {
        get
        {
            var peak = -1;
            var peakCount = 0UL;
            for (var hour = 0; hour < HourCounts.Count; hour++)
            {
                if (HourCounts[hour] > peakCount)
                {
                    peakCount = HourCounts[hour];
                    peak = hour;
                }
            }

            return peak;
        }
    }
}

/// <summary>一段时间内的逐日汇总，附带常用聚合值。</summary>
public sealed class DailyUsageSummary(IReadOnlyList<DailyUsage> days)
{
    public static DailyUsageSummary Empty { get; } = new([]);

    public IReadOnlyList<DailyUsage> Days { get; } = days;

    public int DayCount => Days.Count;

    public int ActiveDays => Days.Count(day => day.IsActive);

    public int FireDays => Days.Count(day => day.IsFire);

    public int TotalActiveMinutes => Days.Sum(day => day.ActiveMinuteCount);

    public ulong TotalCount => Days.Aggregate(0UL, (sum, day) => checked(sum + day.TotalCount));

    /// <summary>按自然日跨度算的日均输入，包含没有输入的日子。</summary>
    public double AveragePerDay => DayCount == 0 ? 0 : TotalCount / (double)DayCount;

    public static DailyUsageSummary From(IEnumerable<DailyUsage> days) => new(days.ToArray());
}
