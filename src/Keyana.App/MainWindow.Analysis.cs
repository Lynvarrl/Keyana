using System.Windows;
using System.Windows.Controls;
using Keyana.App.Controls;
using Keyana.Core;
using Keyana.Storage;

namespace Keyana.App;

public partial class MainWindow
{
    private void InitializeAnalysisDefaults()
    {
        var range = TimeRangeCalculator.ForPreset(TimeRangePreset.Recent30Days, DateTimeOffset.Now);
        _analysisRange = range;
        _analysisPreset = TimeRangePreset.Recent30Days;
        AnalysisRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        AnalysisRangeHintText.Text = $"{FormatRangeShort(range)} · 独立于概览页";
    }

    private async void AnalysisPresetChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || _syncingAnalysisPresetChips || sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<TimeRangePreset>(tag, out var preset))
        {
            return;
        }

        await ApplyAnalysisPresetAsync(preset);
    }

    private async Task ApplyAnalysisPresetAsync(TimeRangePreset preset)
    {
        _analysisPreset = preset;
        await SyncAnalysisRangeFromPresetAsync();
        await RefreshAnalysisAsync(force: true);
    }

    /// <summary>
    /// 按当前预设重算分析区间并同步到界面。数据库就绪前点「全部」拿不到历史边界，
    /// 只能得到当前一分钟，等存储初始化完再调一次即可补回来。
    /// </summary>
    private async Task SyncAnalysisRangeFromPresetAsync()
    {
        var range = await ResolveAnalysisPresetRangeAsync(_analysisPreset);
        _analysisRange = range;
        // 用户正在日历里选新的起止时不要回写，否则每 5 秒会把他的编辑冲掉。
        if (!AnalysisRangePicker.IsEditorOpen)
        {
            AnalysisRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        }

        UpdateAnalysisHint();
    }

    private async Task<(long StartUtc, long EndUtc)> ResolveAnalysisPresetRangeAsync(TimeRangePreset preset)
    {
        StoredDataRange? storedRange = null;
        if (preset == TimeRangePreset.All && _repository is not null)
        {
            storedRange = await _repository.GetDataRangeAsync(_lifetimeCancellation.Token);
        }

        return TimeRangeCalculator.ForPreset(preset, DateTimeOffset.Now, storedRange);
    }

    private async void AnalysisRangePicker_RangeApplied(object? sender, EventArgs eventArgs)
    {
        _customAnalysisInput = (AnalysisRangePicker.RangeStart, AnalysisRangePicker.RangeEnd);
        await ApplyAnalysisCustomRangeAsync();
    }

    private async Task ApplyAnalysisCustomRangeAsync()
    {
        if (_customAnalysisInput is not { } input)
        {
            return;
        }

        var range = ResolveCustomRange(input);
        if (range.EndUtc <= range.StartUtc)
        {
            AnalysisRangeHintText.Text = "结束时间必须晚于开始时间。";
            return;
        }

        _analysisRange = range;
        _analysisPreset = TimeRangePreset.Custom;
        AnalysisRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        ClearAnalysisPresetChips();
        UpdateAnalysisHint();
        await RefreshAnalysisAsync(force: true);
    }

    /// <summary>
    /// 分析页的副标题：区间 + 「独立于概览页」。
    /// 筛选状态不写在这里：这一行右侧紧挨着范围 chip，再塞一段会被省略号截断；
    /// 筛选标记放在「按键筛选」按钮和区间总输入的副标题上更清楚。
    /// </summary>
    private void UpdateAnalysisHint()
    {
        if (_analysisRange is not { } range)
        {
            return;
        }

        AnalysisRangeHintText.Text = $"{FormatRangeShort(range)} · 独立于概览页";
    }

    private void ClearAnalysisPresetChips()
    {
        _syncingAnalysisPresetChips = true;
        try
        {
            AnalysisPresetAll.IsChecked = false;
            AnalysisPresetRecent30Days.IsChecked = false;
            AnalysisPresetRecent7Days.IsChecked = false;
            AnalysisPresetYesterday.IsChecked = false;
            AnalysisPresetToday.IsChecked = false;
        }
        finally
        {
            _syncingAnalysisPresetChips = false;
        }
    }

    private async Task RefreshAnalysisAsync(bool force = false)
    {
        if (_analysisRefreshRunning || _repository is null)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastAnalysisUiRefresh < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _analysisRefreshRunning = true;
        try
        {
            // 分析页可能整夜挂在屏幕上：相对预设每次刷新都按当前时钟重算区间，
            // 否则「近 30 天」会停在打开页面那一刻，跨天后少算一天。
            if (_analysisPreset != TimeRangePreset.Custom)
            {
                await SyncAnalysisRangeFromPresetAsync();
            }

            if (_analysisRange is not { } range)
            {
                return;
            }

            // 分析页是「看结果」的地方，先把当前这一分钟落盘，避免刚切过来时数字偏小。
            if (force && _recorder is not null)
            {
                try
                {
                    await _recorder.FlushNowAsync(_lifetimeCancellation.Token);
                }
                catch (Exception) when (!_isClosing)
                {
                }
            }

            var aggregate = await _repository.QueryAggregateAsync(
                range.StartUtc,
                range.EndUtc,
                _lifetimeCancellation.Token);
            var daily = await _repository.QueryDailyUsageAsync(
                range.StartUtc,
                range.EndUtc,
                TimeZoneInfo.Local,
                _lifetimeCancellation.Token);

            // 原始计数留给筛选面板显示每个键的真实次数，展示与指标用过滤后的副本。
            _analysisRawCounts = WithoutWheelDistance(aggregate.Counts);
            var visibleCounts = KeyFilter.Visible(aggregate.Counts, _analysisFilter) ?? aggregate.Counts;
            // 滚轮距离键承载的是格数，不是次数：从这里开始就排除，
            // 排行榜、类别占比、总输入与日均一律不把它当「输入次数」。
            var counted = WithoutWheelDistance(visibleCounts);
            var total = counted.Aggregate(0UL, (sum, count) => checked(sum + count));

            AnalysisTotalText.Text = total.ToString("N0");
            // 有筛选时日均按「筛选后总量 ÷ 自然日数」算：活跃天数与使用时长仍取未筛选口径，
            // 这是筛选只作用于输入量的必然结果，提示行里也标了「已筛选」。
            AnalysisAverageText.Text = daily.DayCount == 0
                ? "0"
                : (total / (double)daily.DayCount).ToString("N0");
            AnalysisTotalHintText.Text = _analysisFilter is null
                ? "含键盘、鼠标按键与滚轮手势"
                : $"已筛选 · {total:N0} 次";
            AnalysisActiveDaysText.Text = daily.ActiveDays.ToString("N0");
            _analysisActiveMinutes = daily.TotalActiveMinutes;
            UpdateDurationTexts();

            RankingList.ItemsSource = BuildRankingRows(counted, total);
            RankingEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            CategoryShare.SetData(BuildCategorySlices(counted));
            var wheelDistance = await _repository.QueryWheelDistanceAsync(
                range.StartUtc,
                range.EndUtc,
                _lifetimeCancellation.Token);
            // 旧行只有格数：折回距离一并展示，避免历史区间显示成 0。
            UpdateAnalysisWheelMetrics(
                wheelDistance + LegacyWheelDistance(aggregate.Counts),
                counted);
            AnalysisDailyStrip.SetPalette(_currentPalette);
            AnalysisDailyStrip.SetData(
                daily.Days.Count > MaxActivityBars
                    ? daily.Days.Skip(daily.Days.Count - MaxActivityBars).ToArray()
                    : daily.Days);
            _lastAnalysisUiRefresh = DateTimeOffset.UtcNow;
            UpdateAnalysisHint();
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                AnalysisRangeHintText.Text = $"查询失败：{exception.Message}";
            }
        }
        finally
        {
            _analysisRefreshRunning = false;
        }
    }

    private static IReadOnlyList<KeyRankRow> BuildRankingRows(ulong[] counts, ulong total)
    {
        return KeyCatalog.Definitions
            .Select(definition => (Definition: definition, Count: counts[(int)definition.Id]))
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => (int)item.Definition.Id)
            .Take(30)
            .Select((item, index) => new KeyRankRow
            {
                Rank = index + 1,
                DisplayName = item.Definition.DisplayName,
                Category = item.Definition.Category,
                Count = item.Count,
                Share = total == 0 ? 0 : item.Count * 100d / total,
            })
            .ToArray();
    }

    private static IReadOnlyList<CategorySlice> BuildCategorySlices(ulong[] counts)
    {
        // 固定顺序让颜色在每次刷新之间保持稳定，不会因为某类计数变化而换色。
        string[] categoryOrder = ["主键区", "功能键", "导航键", "数字键盘", "鼠标", "扩展键"];
        var totals = KeyCatalog.Definitions
            .GroupBy(definition => definition.Category)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0UL, (sum, definition) => checked(sum + counts[(int)definition.Id])));

        return categoryOrder
            .Select(category => new CategorySlice(category, totals.TryGetValue(category, out var value) ? value : 0))
            .ToArray();
    }
}
