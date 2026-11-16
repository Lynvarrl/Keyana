using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyStats.App.Controls;
using KeyStats.Core;
using KeyStats.Storage;

namespace KeyStats.App;

public partial class MainWindow
{
    private async void PresetChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<TimeRangePreset>(tag, out var preset))
        {
            return;
        }

        // 「自定义」不在这里应用：它由 Click 事件展开日历，等用户点「应用」后再切换区间。
        if (preset == TimeRangePreset.Custom)
        {
            return;
        }

        if (preset == _activeRangePreset && _selectedRange is not null)
        {
            return;
        }

        _activeRangePreset = preset;
        RangeValidationText.Text = string.Empty;
        await ApplyActivePresetAsync();
    }

    private void CustomChip_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_uiReady)
        {
            OverviewRangePicker.Open();
        }
    }

    private async void OverviewRangePicker_RangeApplied(object? sender, EventArgs eventArgs)
    {
        var range = TimeRangeCalculator.ForCustom(
            OverviewRangePicker.RangeStart,
            OverviewRangePicker.RangeEnd,
            _settings?.SnapToTenMinutes ?? true);

        if (range.EndUtc <= range.StartUtc)
        {
            RangeValidationText.Text = "结束时间必须晚于开始时间。";
            return;
        }

        _activeRangePreset = TimeRangePreset.Custom;
        SelectPresetChip(TimeRangePreset.Custom);
        _selectedRange = range;
        // 10 分钟吸附会把 12:41 扩到 12:50，把按钮文字同步成真实查询范围，避免两处对不上。
        SyncRangePicker(range);
        RangeValidationText.Text = string.Empty;
        await RefreshSelectedRangeAsync(force: true);
    }

    private void SelectPresetChip(TimeRangePreset preset)
    {
        var chip = preset switch
        {
            TimeRangePreset.Today => PresetChipToday,
            TimeRangePreset.Yesterday => PresetChipYesterday,
            TimeRangePreset.Recent10 => PresetChipRecent10,
            TimeRangePreset.Recent7Days => PresetChipRecent7Days,
            TimeRangePreset.Recent30Days => PresetChipRecent30Days,
            TimeRangePreset.All => PresetChipAll,
            _ => PresetChipCustom,
        };

        if (chip.IsChecked != true)
        {
            chip.IsChecked = true;
        }
    }

    private async Task ApplyActivePresetAsync()
    {
        if (_repository is null && _activeRangePreset == TimeRangePreset.All)
        {
            return;
        }

        var range = await ResolvePresetRangeAsync(_activeRangePreset);
        _selectedRange = range;
        SyncRangePicker(range);
        await RefreshSelectedRangeAsync(force: true);
    }

    private async Task<(long StartUtc, long EndUtc)> ResolvePresetRangeAsync(TimeRangePreset preset)
    {
        // 只有「全部」需要读库拿历史边界，其余预设是纯计算，交给 TimeRangeCalculator。
        StoredDataRange? storedRange = null;
        if (preset == TimeRangePreset.All && _repository is not null)
        {
            storedRange = await _repository.GetDataRangeAsync(_lifetimeCancellation.Token);
        }

        return TimeRangeCalculator.ForPreset(preset, DateTimeOffset.Now, storedRange);
    }

    private void SyncRangePicker((long StartUtc, long EndUtc) range)
    {
        if (!_uiReady || OverviewRangePicker.IsEditorOpen)
        {
            return;
        }

        OverviewRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
    }

    private async Task RefreshSelectedRangeAsync(bool force = false)
    {
        if (_heatmapRefreshRunning || _repository is null || _selectedRange is not { } range)
        {
            return;
        }

        if (!force && _lastHeatmapUiRefresh != DateTimeOffset.MinValue)
        {
            return;
        }

        _heatmapRefreshRunning = true;
        try
        {
            var storedCounts = new ulong[KeyCatalog.Count];
            var storedTimeCounts = new ulong[TenMinuteTimelineControl.SegmentCount];
            var liveSnapshot = _recorder?.GetCurrentSnapshot();
            if (liveSnapshot is not null &&
                liveSnapshot.BucketStartUtc >= range.StartUtc &&
                liveSnapshot.BucketStartUtc < range.EndUtc)
            {
                await AccumulateStoredRangeAsync(range.StartUtc, liveSnapshot.BucketStartUtc, storedCounts);
                await AccumulateStoredTimeRangeAsync(range.StartUtc, liveSnapshot.BucketStartUtc, storedTimeCounts);
                await AccumulateStoredRangeAsync(
                    liveSnapshot.BucketStartUtc + TimeBuckets.MinuteSeconds,
                    range.EndUtc,
                    storedCounts);
                await AccumulateStoredTimeRangeAsync(
                    liveSnapshot.BucketStartUtc + TimeBuckets.MinuteSeconds,
                    range.EndUtc,
                    storedTimeCounts);
                _selectedLiveBucketStart = liveSnapshot.BucketStartUtc;
                _lastLiveCounts = (uint[])liveSnapshot.Counts.Clone();
            }
            else
            {
                await AccumulateStoredRangeAsync(range.StartUtc, range.EndUtc, storedCounts);
                await AccumulateStoredTimeRangeAsync(range.StartUtc, range.EndUtc, storedTimeCounts);
                _selectedLiveBucketStart = null;
                _lastLiveCounts = null;
            }

            _selectedStoredCounts = storedCounts;
            _selectedStoredTimeCounts = storedTimeCounts;
            var displayCounts = (ulong[])storedCounts.Clone();
            var displayTimeCounts = (ulong[])storedTimeCounts.Clone();
            if (liveSnapshot is not null && _selectedLiveBucketStart == liveSnapshot.BucketStartUtc)
            {
                AddCounts(displayCounts, liveSnapshot.Counts);
                AddLiveTimeCount(displayTimeCounts, liveSnapshot);
            }

            ApplySelectedCounts(displayCounts, displayTimeCounts);
            await RefreshRangeMetricsAsync(range);
            await RefreshActivityUiAsync();
            var pressedKeys = _counter.GetSnapshot().PressedKeys;
            RenderHeatmap(pressedKeys);
            HeatmapRangeText.Text = FormatRange(range);
            HeatmapUpdatedText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
            _lastHeatmapUiRefresh = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                RangeValidationText.Text = $"查询失败：{exception.Message}";
            }
        }
        finally
        {
            _heatmapRefreshRunning = false;
        }
    }

    private async Task RefreshRangeMetricsAsync((long StartUtc, long EndUtc) range)
    {
        if (_repository is null)
        {
            return;
        }

        var daily = await _repository.QueryDailyUsageAsync(
            range.StartUtc,
            range.EndUtc,
            TimeZoneInfo.Local,
            _lifetimeCancellation.Token);

        _rangeActiveMinutes = daily.TotalActiveMinutes;
        UsageDurationText.Text = FormatDuration(_rangeActiveMinutes, _durationUsesDays);
    }

    /// <summary>
    /// 主统计范围每两秒会刷新一次，但每日活跃区间是独立的，没必要跟着重扫一遍几十天的
    /// 分钟数据：只把「与主范围重合的日期」重画一遍即可。数据本身按低频节奏刷新，
    /// 保证跨天或长时间挂机后柱子仍然是最新的。
    /// </summary>
    private async Task RefreshActivityUiAsync()
    {
        if (DateTimeOffset.UtcNow - _lastActivityUiRefresh < TimeSpan.FromSeconds(60))
        {
            var (start, end) = SelectedRangeLocalDates();
            ActivityStrip.SetHighlight(start, end);
            return;
        }

        await RefreshActivityMetricsAsync();
    }

    /// <summary>
    /// 每日活跃区间的指标与柱状图。这个区间独立于主统计范围：
    /// 主范围只有一天时，「活跃天数」仍然应该看近 30 天，否则永远是 0 或 1。
    /// </summary>
    private async Task RefreshActivityMetricsAsync()
    {
        if (_repository is null || _activityRange is not { } range)
        {
            return;
        }

        var daily = await _repository.QueryDailyUsageAsync(
            range.StartUtc,
            range.EndUtc,
            TimeZoneInfo.Local,
            _lifetimeCancellation.Token);

        var label = ActivityRangeLabel();
        ActiveDaysText.Text = daily.ActiveDays.ToString("N0");
        FireDaysText.Text = daily.FireDays.ToString("N0");
        ActiveDaysHintText.Text = $"{label} · ≥325 次/天";
        FireDaysHintText.Text = $"{label} · ≥7999 次/天";

        // 区间很长时只画最后 90 天，否则会生成几千根柱子，既慢又看不清。
        var days = daily.Days;
        var shown = days.Count > 90 ? days.Skip(days.Count - 90).ToArray() : days;
        var highlight = SelectedRangeLocalDates();
        ActivityStrip.SetData(shown, highlight.Start, highlight.End);
        ActivitySummaryText.Text = shown.Count == 0
            ? "所选区间暂无数据"
            : days.Count > 90
                ? $"最近 90 天（共 {days.Count} 天）"
                : $"{shown[0].Date:MM-dd} — {shown[^1].Date:MM-dd} · 共 {shown.Count} 天";
        _lastActivityUiRefresh = DateTimeOffset.UtcNow;
    }

    private void InitializeActivityDefaults()
    {
        var range = TimeRangeCalculator.ForPreset(TimeRangePreset.Recent30Days, DateTimeOffset.Now);
        _activityRange = range;
        ActivityRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        ActivitySummaryText.Text = $"{FormatRangeShort(range)} · 每日活跃区间";
    }

    private async void ActivityRangePicker_RangeApplied(object? sender, EventArgs eventArgs)
    {
        var range = TimeRangeCalculator.ForCustom(
            ActivityRangePicker.RangeStart,
            ActivityRangePicker.RangeEnd,
            _settings?.SnapToTenMinutes ?? true);

        if (range.EndUtc <= range.StartUtc)
        {
            return;
        }

        _activityRange = range;
        _activityPreset = TimeRangePreset.Custom;
        ActivityRangePicker.SetRangeUtc(range.StartUtc, range.EndUtc);
        await RefreshActivityMetricsAsync();
    }

    private string ActivityRangeLabel() => _activityPreset switch
    {
        TimeRangePreset.Today => "今日",
        TimeRangePreset.Yesterday => "昨日",
        TimeRangePreset.Recent7Days => "近 7 天",
        TimeRangePreset.Recent30Days => "近 30 天",
        TimeRangePreset.All => "全部",
        _ => "自定义区间",
    };

    private (DateOnly? Start, DateOnly? End) SelectedRangeLocalDates()
    {
        if (_selectedRange is not { } range)
        {
            return (null, null);
        }

        return (
            DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(range.StartUtc).ToLocalTime().DateTime),
            DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(range.EndUtc - 1).ToLocalTime().DateTime));
    }

    private void UsageDurationCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ClickCount != 2)
        {
            return;
        }

        _durationUsesDays = !_durationUsesDays;
        UpdateDurationTexts();
        eventArgs.Handled = true;
    }

    private void UpdateDurationTexts()
    {
        UsageDurationText.Text = FormatDuration(_rangeActiveMinutes, _durationUsesDays);
        AnalysisDurationText.Text = FormatDuration(_analysisActiveMinutes, _durationUsesDays);
        // 不足一天时两种单位显示结果相同，hint 里把当前模式写出来，双击才有反馈。
        var mode = _durationUsesDays ? "最大单位 天（双击切换）" : "最大单位 小时（双击切换）";
        UsageDurationHintText.Text = $"所选范围 · {mode}";
        AnalysisDurationHintText.Text = $"所选区间 · {mode}";
    }

    private bool RefreshLiveSelectedCounts()
    {
        if (_recorder is null || _selectedRange is not { } range || _selectedLiveBucketStart is null)
        {
            return false;
        }

        var liveSnapshot = _recorder.GetCurrentSnapshot();
        if (liveSnapshot.BucketStartUtc != _selectedLiveBucketStart)
        {
            _ = RefreshStorageUiAsync(force: true);
            return false;
        }

        if (liveSnapshot.BucketStartUtc < range.StartUtc || liveSnapshot.BucketStartUtc >= range.EndUtc ||
            (_lastLiveCounts is not null && _lastLiveCounts.AsSpan().SequenceEqual(liveSnapshot.Counts)))
        {
            return false;
        }

        _lastLiveCounts = (uint[])liveSnapshot.Counts.Clone();
        var displayCounts = (ulong[])_selectedStoredCounts.Clone();
        var displayTimeCounts = (ulong[])_selectedStoredTimeCounts.Clone();
        AddCounts(displayCounts, liveSnapshot.Counts);
        AddLiveTimeCount(displayTimeCounts, liveSnapshot);
        ApplySelectedCounts(displayCounts, displayTimeCounts);
        return true;
    }

    private void ApplySelectedCounts(ulong[] counts, ulong[] timeCounts)
    {
        _selectedCounts = counts;
        _selectedDisplayTimeCounts = timeCounts;
        var totalCount = counts.Aggregate(0UL, (sum, count) => checked(sum + count));
        HeatmapTotalText.Text = $"所选范围总计：{totalCount:N0}";
        RangeTotalText.Text = totalCount.ToString("N0");
        RangeTotalHintText.Text = _selectedRange is { } range ? FormatRangeShort(range) : "—";

        MouseLeftText.Text = counts[(int)KeyId.MouseLeftButton].ToString("N0");
        MouseRightText.Text = counts[(int)KeyId.MouseRightButton].ToString("N0");
        MouseMiddleText.Text = counts[(int)KeyId.MouseMiddleButton].ToString("N0");
        WheelUpText.Text = counts[(int)KeyId.MouseWheelUp].ToString("N0");
        WheelDownText.Text = counts[(int)KeyId.MouseWheelDown].ToString("N0");
        var mouseTotal = checked(
            counts[(int)KeyId.MouseLeftButton] +
            counts[(int)KeyId.MouseRightButton] +
            counts[(int)KeyId.MouseMiddleButton] +
            counts[(int)KeyId.MouseWheelUp] +
            counts[(int)KeyId.MouseWheelDown]);
        MouseTotalText.Text = mouseTotal.ToString("N0");

        foreach (var row in _rows)
        {
            row.Count = counts[(int)row.Id];
        }

        if (ActiveOnlyCheckBox.IsChecked == true)
        {
            _rowsView.Refresh();
        }

        DailyTimeline.SetData(timeCounts, _currentPalette);
        var peakCount = timeCounts.Max();
        if (peakCount == 0)
        {
            DailyTimelineSummaryText.Text = "暂无输入";
        }
        else
        {
            var peakIndex = Array.IndexOf(timeCounts, peakCount);
            var startMinutes = peakIndex * 10;
            var endMinutes = startMinutes + 10;
            DailyTimelineSummaryText.Text =
                $"峰值 {FormatTimelineTime(startMinutes)}—{FormatTimelineTime(endMinutes)} · {peakCount:N0} 次";
        }
    }

    private static void AddLiveTimeCount(ulong[] destination, MinuteBucketSnapshot snapshot)
    {
        var localTime = DateTimeOffset.FromUnixTimeSeconds(snapshot.BucketStartUtc).ToLocalTime();
        var index = (localTime.Hour * 6) + (localTime.Minute / 10);
        destination[index] = checked(destination[index] + (ulong)snapshot.TotalCount);
    }

    private async Task AccumulateStoredRangeAsync(long startUtc, long endUtc, ulong[] destination)
    {
        if (_repository is null || startUtc >= endUtc)
        {
            return;
        }

        var aggregate = await _repository.QueryAggregateAsync(startUtc, endUtc, _lifetimeCancellation.Token);
        AddCounts(destination, aggregate.Counts);
    }

    private async Task AccumulateStoredTimeRangeAsync(long startUtc, long endUtc, ulong[] destination)
    {
        if (_repository is null || startUtc >= endUtc)
        {
            return;
        }

        var series = await _repository.QueryTenMinuteTotalsAsync(
            startUtc,
            endUtc,
            _lifetimeCancellation.Token);
        foreach (var bucket in series)
        {
            var localTime = DateTimeOffset.FromUnixTimeSeconds(bucket.BucketStartUtc).ToLocalTime();
            var index = (localTime.Hour * 6) + (localTime.Minute / 10);
            destination[index] = checked(destination[index] + bucket.TotalCount);
        }
    }

    private static void AddCounts(ulong[] destination, IReadOnlyList<ulong> source)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = checked(destination[index] + source[index]);
        }
    }

    private static void AddCounts(ulong[] destination, IReadOnlyList<uint> source)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = checked(destination[index] + source[index]);
        }
    }

    private void RenderHeatmap(IReadOnlySet<KeyId> pressedKeys) =>
        HeatmapKeyboard.SetData(_selectedCounts, pressedKeys, _currentScaleMode, _currentPalette);

    private void ScaleChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady)
        {
            return;
        }

        _currentScaleMode = sender is RadioButton { Tag: "Linear" }
            ? HeatScaleMode.Linear
            : HeatScaleMode.SquareRoot;
        RenderHeatmap(_counter.GetSnapshot().PressedKeys);
    }

    private void PaletteChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || !_paletteReady)
        {
            return;
        }

        _currentPalette = sender is RadioButton { Tag: "GreenAmber" }
            ? HeatPalette.GreenAmber
            : HeatPalette.BlueMagenta;

        UpdateGradientBar();
        RenderHeatmap(_counter.GetSnapshot().PressedKeys);
        DailyTimeline.SetData(_selectedDisplayTimeCounts, _currentPalette);

        if (_settings is not null)
        {
            _settings.HeatPalette = _currentPalette.ToString();
            _ = SaveSettingsAsync();
        }
    }

    private void UpdateGradientBar()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.5),
            EndPoint = new System.Windows.Point(1, 0.5),
        };

        const int stops = 12;
        for (var step = 0; step <= stops; step++)
        {
            var position = step / (double)stops;
            var color = HeatPaletteColors.GetColor(_currentPalette, step == 0 ? 0UL : 1UL, position);
            brush.GradientStops.Add(new GradientStop(color, position));
        }

        GradientBar.Background = brush;
    }

    private static string FormatTimelineTime(int totalMinutes) =>
        totalMinutes == 24 * 60
            ? "24:00"
            : $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";

    private static string FormatDuration(int minutes, bool useDays)
    {
        if (minutes <= 0)
        {
            return "0 分";
        }

        if (minutes < 60)
        {
            return $"{minutes} 分";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;

        // 小时模式始终以小时为最大单位；天模式在不足 24 小时时也用小时，超过才进位。
        if (!useDays || hours < 24)
        {
            return rest == 0 ? $"{hours} 小时" : $"{hours} 小时 {rest} 分";
        }

        var days = hours / 24;
        var remainingHours = hours % 24;
        var text = $"{days} 天";
        if (remainingHours > 0)
        {
            text += $" {remainingHours} 小时";
        }

        if (rest > 0)
        {
            text += $" {rest} 分";
        }

        return text;
    }

    private static string FormatRange((long StartUtc, long EndUtc) range)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(range.StartUtc).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeSeconds(range.EndUtc).ToLocalTime();
        return $"{start:yyyy-MM-dd HH:mm} — {end:yyyy-MM-dd HH:mm}（结束不含）";
    }

    private static string FormatRangeShort((long StartUtc, long EndUtc) range)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(range.StartUtc).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeSeconds(range.EndUtc).ToLocalTime();
        return start.Date == end.Date
            ? $"{start:MM-dd HH:mm} → {end:HH:mm}"
            : $"{start:MM-dd HH:mm} → {end:MM-dd HH:mm}";
    }
}
