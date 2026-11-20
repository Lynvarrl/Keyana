using System.Windows;
using System.Windows.Controls;
using Keyana.Core;
using Keyana.Storage;

namespace Keyana.App;

/// <summary>
/// 按键筛选：概览与分析各自持有一份筛选状态，互不影响，只在需要时用面板里的
/// 「同步到另一页」复制过去。
///
/// 筛选完全发生在展示层：每次渲染前把未选中的键清零，数据库与内存里的原始计数都不动，
/// 所以取消筛选是瞬时的，也不需要重查历史。指标口径按用户要求区分：
/// 总输入、日均、排行榜、类别占比跟着筛选走；活跃天数、使用时长与每日趋势不跟着走
/// （它们按自然日/分钟统计，数据库里没有「每天的每个键」这一层聚合）。
/// </summary>
public partial class MainWindow
{
    private void OverviewFilterButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        OverviewFilterPanel.SetCounts(_selectedRawCounts);
        OverviewFilterPanel.SetSelection(_overviewFilter);
        OverviewFilterPopup.IsOpen = true;
    }

    private void AnalysisFilterButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        AnalysisFilterPanel.SetCounts(_analysisRawCounts);
        AnalysisFilterPanel.SetSelection(_analysisFilter);
        AnalysisFilterPopup.IsOpen = true;
    }

    private void ApplyOverviewFilter()
    {
        _overviewFilter = OverviewFilterPanel.Selection;
        UpdateFilterButton(OverviewFilterButton, _overviewFilter);

        // 不重查数据库：stored + 当前分钟的实时增量都在字段里，重新算一遍即可。
        ReapplySelectedCountsFromCache();
    }

    private void ApplyAnalysisFilter()
    {
        _analysisFilter = AnalysisFilterPanel.Selection;
        UpdateFilterButton(AnalysisFilterButton, _analysisFilter);
        if (!_uiReady)
        {
            return;
        }

        _ = RefreshAnalysisAsync(force: true);
    }

    /// <summary>
    /// 用缓存的「已落盘计数 + 当前分钟实时计数」重算一次展示，供筛选切换时即时反馈。
    /// 与 <c>RefreshSelectedRangeAsync</c> 的合并顺序保持一致。
    /// </summary>
    private void ReapplySelectedCountsFromCache()
    {
        if (!_uiReady)
        {
            return;
        }

        var displayCounts = (ulong[])_selectedStoredCounts.Clone();
        var displayTimeCounts = (ulong[])_selectedStoredTimeCounts.Clone();
        if (_recorder?.GetCurrentSnapshot() is { } live && _selectedLiveBucketStart == live.BucketStartUtc)
        {
            AddCounts(displayCounts, live.Counts);
            AddLiveTimeCount(displayTimeCounts, live);
        }

        ApplySelectedCounts(displayCounts, displayTimeCounts);
        RenderHeatmap(_counter.GetSnapshot().PressedKeys);
    }

    /// <summary>把当前页的筛选复制到另一页，并关闭浮层。</summary>
    private void SyncFilter(bool overviewToAnalysis)
    {
        if (overviewToAnalysis)
        {
            _analysisFilter = _overviewFilter?.ToHashSet();
            AnalysisFilterPanel.SetSelection(_analysisFilter);
            UpdateFilterButton(AnalysisFilterButton, _analysisFilter);
            OverviewFilterPopup.IsOpen = false;
            if (_uiReady)
            {
                _ = RefreshAnalysisAsync(force: true);
            }

            return;
        }

        _overviewFilter = _analysisFilter?.ToHashSet();
        OverviewFilterPanel.SetSelection(_overviewFilter);
        UpdateFilterButton(OverviewFilterButton, _overviewFilter);
        AnalysisFilterPopup.IsOpen = false;
        ReapplySelectedCountsFromCache();
    }

    private static void UpdateFilterButton(Button button, IReadOnlySet<KeyId>? filter)
    {
        button.Content = filter is null ? "按键筛选" : KeyFilter.Describe(filter);
        button.Tag = filter is null ? null : "Active";
    }
}
