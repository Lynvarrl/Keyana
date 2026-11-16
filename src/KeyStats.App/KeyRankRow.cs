namespace KeyStats.App;

/// <summary>分析页排行榜的一行。占比是 0～100 的百分数，直接给进度条用。</summary>
public sealed class KeyRankRow
{
    public required int Rank { get; init; }

    public required string DisplayName { get; init; }

    public required string Category { get; init; }

    public required ulong Count { get; init; }

    public required double Share { get; init; }

    public string CountText => Count.ToString("N0");

    public string ShareText => Share >= 10 ? $"{Share:F1}%" : $"{Share:F2}%";
}
