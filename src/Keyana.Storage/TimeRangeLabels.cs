namespace Keyana.Storage;

/// <summary>
/// 时间范围预设的界面叫法。
///
/// 统一用「日」：这些字符串描述的是一个日期区间（今日、近 7 日、全部 87 日），
/// 而「活跃天数」「共 87 天」「≥325 次/天」描述的是数量与频率，两者不能混写成
/// 一个词。放在 Storage 层是为了能单独测——界面上一旦重新出现「近 7 天」，
/// 回归测试会直接失败。
/// </summary>
public static class TimeRangeLabels
{
    /// <summary>
    /// 取预设的显示名。
    /// </summary>
    /// <param name="preset">要显示的区间预设。</param>
    /// <param name="dayCount">
    /// 区间覆盖的自然日数。只有「全部」会用到：它没有固定跨度，不写出天数就看不出
    /// 这一段到底有多长；传入 0 表示天数未知，此时只显示「全部」。
    /// </param>
    public static string ForPreset(TimeRangePreset preset, int dayCount = 0) => preset switch
    {
        TimeRangePreset.Today => "今日",
        TimeRangePreset.Yesterday => "昨日",
        TimeRangePreset.Recent10 => "最近 10 分钟",
        TimeRangePreset.Recent7Days => "近 7 日",
        TimeRangePreset.Recent30Days => "近 30 日",
        TimeRangePreset.Recent90Days => "近 90 日",
        // 天数紧贴文字不空格：这串要接在「· ≥7999 次/天」前面，卡片里只有
        // 118 DIP 的宽度，多两个空格就会把末尾的「天」挤到卡片外。
        TimeRangePreset.All => dayCount > 0 ? $"全部{dayCount:N0}日" : "全部",
        _ => "自定义区间",
    };
}
