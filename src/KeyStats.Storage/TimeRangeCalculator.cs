namespace KeyStats.Storage;

/// <summary>界面上可选的时间范围预设。</summary>
public enum TimeRangePreset
{
    Today,
    Yesterday,
    Recent10,
    Recent7Days,
    Recent30Days,
    All,
    Custom,
}

/// <summary>
/// 把「范围预设」换算成 UTC 的分钟对齐区间。
///
/// 这段逻辑原本埋在 MainWindow.xaml.cs 里，和仓储、控件、计时器混在一起，
/// 既没法单独测试也无法复用。挪到 Storage 层后只依赖输入参数，不碰界面。
///
/// 约定：
/// - 所有区间都是左闭右开，且两端对齐到整分钟；
/// - 「今天」按给定时区的当日零点算，不是 UTC 零点；
/// - 「全部」在没有历史数据时退化为当前分钟，有数据时向两侧取并集，
///   保证正在累计的这一分钟也算进去。
/// </summary>
public static class TimeRangeCalculator
{
    /// <summary>
    /// 计算预设区间。<paramref name="localNow"/> 所在的时区优先取
    /// <paramref name="timeZone"/>，未指定时用本机时区。
    /// </summary>
    public static (long StartUtc, long EndUtc) ForPreset(
        TimeRangePreset preset,
        DateTimeOffset localNow,
        StoredDataRange? storedRange = null,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var currentMinute = TimeBuckets.ToMinuteStart(new DateTimeOffset(localNow.UtcDateTime, TimeSpan.Zero));
        var endUtc = currentMinute + TimeBuckets.MinuteSeconds;

        return preset switch
        {
            TimeRangePreset.Yesterday => Yesterday(zone, localNow),
            TimeRangePreset.Recent10 => (endUtc - TimeBuckets.TenMinuteSeconds, endUtc),
            TimeRangePreset.Recent7Days => (endUtc - (7L * 24 * 60 * TimeBuckets.MinuteSeconds), endUtc),
            TimeRangePreset.Recent30Days => (endUtc - (30L * 24 * 60 * TimeBuckets.MinuteSeconds), endUtc),
            TimeRangePreset.All => storedRange is null
                ? (currentMinute, endUtc)
                : (Math.Min(storedRange.StartUtc, currentMinute), Math.Max(storedRange.EndUtc, endUtc)),
            _ => Today(zone, localNow),
        };
    }

    /// <summary>
    /// 计算自定义区间。两端都是「不带时区的本地时间」，按 <paramref name="timeZone"/>
    /// 换算成 UTC；<paramref name="alignToTenMinutes"/> 为真时向外扩到十分钟边界。
    /// </summary>
    /// <remarks>
    /// 这里不校验先后顺序：结束早于开始的提示文案属于界面职责，
    /// 调用方拿到结果后自行判断，保持本方法的纯粹性。
    /// </remarks>
    public static (long StartUtc, long EndUtc) ForCustom(
        DateTime startLocal,
        DateTime endLocal,
        bool alignToTenMinutes,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var start = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);

        if (alignToTenMinutes)
        {
            var unit = TimeBuckets.TenMinuteSeconds / TimeBuckets.MinuteSeconds;
            start = AlignLocalDown(start, unit);
            end = AlignLocalUp(end, unit);
        }

        var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, zone), TimeSpan.Zero);
        return (TimeBuckets.ToMinuteStart(startUtc), TimeBuckets.ToMinuteStart(endUtc));
    }

    private static (long StartUtc, long EndUtc) Today(TimeZoneInfo zone, DateTimeOffset localNow)
    {
        var local = TimeZoneInfo.ConvertTime(localNow, zone);
        var dayStartUtc = LocalMidnightUtc(zone, local.Date);

        var currentMinute = TimeBuckets.ToMinuteStart(new DateTimeOffset(localNow.UtcDateTime, TimeSpan.Zero));
        return (TimeBuckets.ToMinuteStart(dayStartUtc), currentMinute + TimeBuckets.MinuteSeconds);
    }

    /// <summary>
    /// 昨天的完整自然日，左闭右开。两端分别按各自的本地零点换算，
    /// 跨夏令时切换日也不会出现 23 或 25 小时的错位。
    /// </summary>
    private static (long StartUtc, long EndUtc) Yesterday(TimeZoneInfo zone, DateTimeOffset localNow)
    {
        var local = TimeZoneInfo.ConvertTime(localNow, zone);
        var yesterdayStart = LocalMidnightUtc(zone, local.Date.AddDays(-1));
        var todayStart = LocalMidnightUtc(zone, local.Date);
        return (TimeBuckets.ToMinuteStart(yesterdayStart), TimeBuckets.ToMinuteStart(todayStart));
    }

    private static DateTimeOffset LocalMidnightUtc(TimeZoneInfo zone, DateTime localDate)
    {
        var midnightLocal = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnightLocal, zone), TimeSpan.Zero);
    }

    private static DateTime AlignLocalDown(DateTime value, int minutes) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute - (value.Minute % minutes), 0, DateTimeKind.Unspecified);

    private static DateTime AlignLocalUp(DateTime value, int minutes)
    {
        var aligned = AlignLocalDown(value, minutes);
        return aligned == value ? aligned : aligned.AddMinutes(minutes);
    }
}
