using System.Globalization;
using System.Windows.Threading;
using Keyana.Core;
using Keyana.Storage;

namespace Keyana.App;

/// <summary>
/// 滚轮双指标的界面接线：手势窗口热更新、校准旁路、距离与手势的展示换算。
///
/// 数据流有两条互相独立的路径：
/// <list type="bullet">
/// <item>入库路径——Raw Input 判定出的距离交给 <see cref="StatisticsRecorder"/>，
/// 手势计数走计数向量的新键位；</item>
/// <item>旁路路径——每条真实事件都送到校准面板，只在面板处于校准态时显示，
/// 全程不碰统计。</item>
/// </list>
/// 两条路径不允许互相借用状态：入库是否暂停由 Raw Input 层按 <c>IsPaused</c> 挡住，
/// 这里不再重复判断，避免出现「两边都以为对方挡了」的漏洞。
/// </summary>
public partial class MainWindow
{
    /// <summary>滑块拖动期间不停写盘的合并窗口。</summary>
    private readonly DispatcherTimer _wheelSettingsSaveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };

    /// <summary>所选范围内已落盘的滚轮距离（不含当前这一分钟）。</summary>
    private WheelDistanceTotals _selectedStoredWheelDistance;

    /// <summary>旧口径折算出的距离：历史行只有「格数」，按每格 120 还原成 delta。</summary>
    private WheelDistanceTotals _selectedLegacyWheelDistance;

    /// <summary>当前这一分钟的实时距离；区间不含这一分钟时为零。</summary>
    private WheelDistanceTotals _lastLiveWheelDistance;

    /// <summary>分析页区间内的滚轮距离。</summary>
    private WheelDistanceTotals _analysisWheelDistance;

    /// <summary>分析页展示用的手势计数（已排除距离键、已应用筛选）。</summary>
    private ulong[] _analysisWheelGestureCounts = new ulong[KeyCatalog.Count];

    private void InitializeWheelUi()
    {
        WheelPanel.WindowChanged += (_, windowMs) => ApplyWheelWindow(windowMs);
        WheelPanel.CalibrationChanged += (_, calibrating) => ApplyWheelCalibration(calibrating);
        WheelPanel.DpiScaleChanged += (_, scale) => ApplyWheelDpiScale(scale);

        _wheelSettingsSaveTimer.Tick += async (_, _) =>
        {
            _wheelSettingsSaveTimer.Stop();
            await SaveSettingsAsync();
        };
    }

    /// <summary>
    /// 手势窗口热更新：内存里的设置、运行中的采集器同步改，不要求重启。
    /// 历史统计不受影响——判定发生在采集那一刻，改窗口只改变之后的事件怎么切分。
    /// </summary>
    private void ApplyWheelWindow(int windowMs)
    {
        var clamped = WheelGestureDetector.ClampWindow(windowMs);
        if (_settings is { } settings && settings.WheelGestureWindowMs != clamped)
        {
            settings.WheelGestureWindowMs = clamped;
            _wheelSettingsSaveTimer.Stop();
            _wheelSettingsSaveTimer.Start();
        }

        if (_rawInput is not null)
        {
            _rawInput.WheelGestureWindowMs = clamped;
        }
    }

    /// <summary>
    /// 校准开关：开启即进入旁路。判定状态必须清空——校准期间的时间戳若留到结束后，
    /// 会把恢复后的第一下手势吞掉。
    /// </summary>
    private void ApplyWheelCalibration(bool calibrating)
    {
        if (_rawInput is null)
        {
            return;
        }

        _rawInput.WheelBypass = calibrating;
        _rawInput.ResetWheelState();
    }

    private void ApplyWheelDpiScale(double scale)
    {
        if (_settings is not { } settings)
        {
            return;
        }

        settings.WheelDpiScale = scale;
        _wheelSettingsSaveTimer.Stop();
        _wheelSettingsSaveTimer.Start();
        // 估算值只影响展示，改完立刻重算两张卡片，不用等下一次查询。
        UpdateWheelMetricsDisplay();
        UpdateAnalysisWheelMetrics(_analysisWheelDistance, _analysisWheelGestureCounts);
    }

    /// <summary>Raw Input 的旁路观测：只喂校准面板，不参与任何统计。</summary>
    private void OnRawWheelObserved(WheelInputInfo info) => WheelPanel.Observe(info);

    /// <summary>需要入库的滚动距离。暂停与校准旁路已在采集层挡掉。</summary>
    private void OnWheelDistanceObserved(WheelInputInfo info) =>
        _recorder?.RecordWheelDistance(info.Direction == WheelDirection.Up, info.Delta);

    private void SetLiveWheelDistance(MinuteBucketSnapshot? snapshot)
    {
        if (snapshot is null || _selectedRange is not { } range ||
            snapshot.BucketStartUtc < range.StartUtc || snapshot.BucketStartUtc >= range.EndUtc)
        {
            _lastLiveWheelDistance = default;
            return;
        }

        _lastLiveWheelDistance = new WheelDistanceTotals(
            snapshot.WheelDistanceUp,
            snapshot.WheelDistanceDown);
    }

    /// <summary>概览「鼠标与滚轮」卡片：距离、估算长度与手势次数。</summary>
    private void UpdateWheelMetricsDisplay()
    {
        var live = _selectedStoredWheelDistance + _lastLiveWheelDistance + _selectedLegacyWheelDistance;
        var distance = live.Total;
        WheelDistanceText.Text = FormatWheelDistance(distance);
        WheelDistanceEstimateText.Text = FormatWheelEstimate(distance);
        WheelGestureText.Text = FormatWheelGestures(
            _selectedCounts[(int)KeyId.WheelGestureUp],
            _selectedCounts[(int)KeyId.WheelGestureDown]);

        // 旧数据只有「格数」，是按 120 折回来的近似值：把这层来源写在悬停提示里，
        // 免得「历史距离」看起来像原始采集出来的精确值。
        WheelDistanceText.ToolTip = _selectedLegacyWheelDistance.Total > 0
            ? $"其中 {WheelMetrics.Notches(_selectedLegacyWheelDistance.Total):N0} 格来自旧版按格记录的滚动，"
              + "已按每格 120 折算为距离，可能与真实 delta 相差不到一格。"
            : null;
    }

    /// <summary>
    /// 旧口径折算：v6 之前滚轮把 delta 按 120 折算成「格」记在 MouseWheelUp/Down 里，
    /// 现在这两列只做历史落点。乘以 120 就能还原成同一量纲的距离——旧数据是向下取整的
    /// 格数，所以最多差不到一格，比直接显示 0 更接近事实。
    ///
    /// 新数据不会写这两个键，因此「距离列」与「旧格数」在任意一分钟里互斥，
    /// 两段相加不会重复计数。
    /// </summary>
    private static WheelDistanceTotals LegacyWheelDistance(IReadOnlyList<ulong> counts)
    {
        var limit = (ulong)(long.MaxValue / WheelMetrics.NotchDelta);
        var up = counts[(int)KeyId.MouseWheelUp];
        var down = counts[(int)KeyId.MouseWheelDown];
        if (up > limit || down > limit)
        {
            return default;
        }

        return new WheelDistanceTotals(
            (long)up * WheelMetrics.NotchDelta,
            (long)down * WheelMetrics.NotchDelta);
    }

    /// <summary>分析页滚轮卡片。</summary>
    private void UpdateAnalysisWheelMetrics(WheelDistanceTotals distance, ulong[] counts)
    {
        _analysisWheelDistance = distance;
        _analysisWheelGestureCounts = counts;
        AnalysisWheelDistanceText.Text = FormatWheelDistance(distance.Total);
        AnalysisWheelEstimateText.Text = FormatWheelEstimate(distance.Total);
        AnalysisWheelGestureText.Text = FormatWheelGestures(
            counts[(int)KeyId.WheelGestureUp],
            counts[(int)KeyId.WheelGestureDown]);
    }

    /// <summary>
    /// 把距离键清零后返回：滚轮距离不是次数，混进「总输入 / 排行榜 / 类别占比」
    /// 就是当初那个「一滑几百格打破数值平衡」的问题复现。
    ///
    /// 只在确实非零时才复制数组，常见路径（新数据里这两列恒为 0）零分配。
    /// </summary>
    private static ulong[] WithoutWheelDistance(ulong[] counts)
    {
        var up = (int)KeyId.MouseWheelUp;
        var down = (int)KeyId.MouseWheelDown;
        if (counts[up] == 0 && counts[down] == 0)
        {
            return counts;
        }

        var copy = (ulong[])counts.Clone();
        copy[up] = 0;
        copy[down] = 0;
        return copy;
    }

    private static string FormatWheelDistance(long distance) =>
        distance <= 0 ? "0 格" : $"{WheelMetrics.Notches(distance):N0} 格";

    /// <summary>「≈ 1.6 m · 估算」：换算只作用于展示，永远不写回存储。</summary>
    private string FormatWheelEstimate(long distance)
    {
        if (distance <= 0)
        {
            return "≈ 0 cm · 估算";
        }

        var centimeters = WheelMetrics.EstimateCentimeters(distance, _settings?.WheelDpiScale ?? 1.0);
        var text = centimeters switch
        {
            < 1 => $"{centimeters * 10:F1} mm",
            < 100 => $"{centimeters:F1} cm",
            _ => $"{centimeters / 100:F2} m",
        };
        return $"≈ {text} · 估算";
    }

    private static string FormatWheelGestures(ulong up, ulong down) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"上滑 {up:N0} · 下滑 {down:N0}");
}
