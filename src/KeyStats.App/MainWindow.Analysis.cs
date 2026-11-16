using System.Windows;
using System.Windows.Controls;
using KeyStats.App.Controls;
using KeyStats.Core;
using KeyStats.Storage;

namespace KeyStats.App;

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
        AnalysisRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        AnalysisRangeHintText.Text = $"{FormatRangeShort(range)} · 独立于概览页";
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
        var range = TimeRangeCalculator.ForCustom(
            AnalysisRangePicker.RangeStart,
            AnalysisRangePicker.RangeEnd,
            _settings?.SnapToTenMinutes ?? true);

        if (range.EndUtc <= range.StartUtc)
        {
            AnalysisRangeHintText.Text = "结束时间必须晚于开始时间。";
            return;
        }

        _analysisRange = range;
        _analysisPreset = TimeRangePreset.Custom;
        AnalysisRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        ClearAnalysisPresetChips();
        AnalysisRangeHintText.Text = $"{FormatRangeShort(range)} · 独立于概览页";
        await RefreshAnalysisAsync(force: true);
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
        if (_analysisRefreshRunning || _repository is null || _analysisRange is not { } range)
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

            var counts = aggregate.Counts;
            var total = aggregate.TotalCount;

            AnalysisTotalText.Text = total.ToString("N0");
            AnalysisAverageText.Text = daily.DayCount == 0 ? "0" : daily.AveragePerDay.ToString("N0");
            AnalysisActiveDaysText.Text = daily.ActiveDays.ToString("N0");
            _analysisActiveMinutes = daily.TotalActiveMinutes;
            AnalysisDurationText.Text = FormatDuration(_analysisActiveMinutes, _durationUsesDays);

            RankingList.ItemsSource = BuildRankingRows(counts, total);
            RankingEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            CategoryShare.SetData(BuildCategorySlices(counts));
            AnalysisDailyStrip.SetData(
                daily.Days.Count > 90 ? daily.Days.Skip(daily.Days.Count - 90).ToArray() : daily.Days);
            _lastAnalysisUiRefresh = DateTimeOffset.UtcNow;
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
