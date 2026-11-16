using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KeyStats.App.Theme;
using KeyStats.Storage;

namespace KeyStats.App.Controls;

/// <summary>
/// 每日活跃图：每根柱子是一天，柱内按 24 小时分段着色，看得到「这一天什么时候在用电脑」。
///
/// 柱高仍按当天总量开方压缩，避免某个爆量日把其它天压成一条线；颜色只表达时段，
/// 不参与活跃/火热的判定，避免两套语义混在一起。
/// 与主统计范围重合的日期会在柱子后面铺一层很淡的强调色，用来对齐两个区间。
/// </summary>
public sealed class DailyActivityControl : Grid
{
    private const double BarAreaHeight = 66;
    private const double MinimumBarHeight = 3;
    private const byte HighlightAlpha = 40;

    /// <summary>0～23 点的时段色带，从凌晨靛蓝过渡到上午青绿、午后橙、夜间紫。</summary>
    private static readonly Color[] HourColors =
    [
        Color.FromRgb(62, 79, 181), Color.FromRgb(61, 92, 196), Color.FromRgb(60, 107, 208),
        Color.FromRgb(59, 124, 216), Color.FromRgb(57, 140, 218), Color.FromRgb(55, 156, 212),
        Color.FromRgb(53, 171, 198), Color.FromRgb(51, 184, 178), Color.FromRgb(52, 195, 156),
        Color.FromRgb(69, 203, 132), Color.FromRgb(99, 208, 108), Color.FromRgb(138, 210, 92),
        Color.FromRgb(180, 209, 82), Color.FromRgb(214, 199, 76), Color.FromRgb(232, 182, 71),
        Color.FromRgb(239, 162, 68), Color.FromRgb(240, 141, 67), Color.FromRgb(237, 117, 71),
        Color.FromRgb(227, 99, 85), Color.FromRgb(212, 90, 110), Color.FromRgb(191, 86, 137),
        Color.FromRgb(164, 84, 163), Color.FromRgb(133, 83, 182), Color.FromRgb(102, 83, 190),
    ];

    private readonly BarLayer _barLayer = new() { IsHitTestVisible = false };
    private readonly UniformGrid _hitLayer = new() { Height = BarAreaHeight, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _startLabel = new();
    private readonly TextBlock _endLabel = new();
    private readonly TextBlock _emptyLabel = new()
    {
        Text = "所选区间暂无数据",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private IReadOnlyList<DailyUsage> _days = [];
    private DateOnly? _highlightStart;
    private DateOnly? _highlightEnd;
    private bool _themeHooked;

    public DailyActivityControl()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarAreaHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

        var barHost = new Grid();
        barHost.Children.Add(_barLayer);
        barHost.Children.Add(_hitLayer);
        _emptyLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        _emptyLabel.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        barHost.Children.Add(_emptyLabel);
        Children.Add(barHost);

        var labels = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _startLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _endLabel.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var label in new[] { _startLabel, _endLabel })
        {
            label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        }

        labels.Children.Add(_startLabel);
        SetColumn(_endLabel, 1);
        labels.Children.Add(_endLabel);
        SetRow(labels, 1);
        Children.Add(labels);

        var legend = new Grid { Margin = new Thickness(0, 1, 0, 0) };
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var legendStart = new TextBlock { Text = "0 点", VerticalAlignment = VerticalAlignment.Center };
        legendStart.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        legendStart.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        legend.Children.Add(legendStart);

        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (var hour = 0; hour < HourColors.Length; hour++)
        {
            gradient.GradientStops.Add(new GradientStop(HourColors[hour], hour / 23d));
        }

        var gradientBar = new Border
        {
            Height = 5,
            Background = gradient,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetColumn(gradientBar, 1);
        legend.Children.Add(gradientBar);

        var legendEnd = new TextBlock { Text = "24 点", VerticalAlignment = VerticalAlignment.Center };
        legendEnd.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        legendEnd.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        SetColumn(legendEnd, 2);
        legend.Children.Add(legendEnd);

        SetRow(legend, 2);
        Children.Add(legend);

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
        Render();
    }

    public void SetData(IReadOnlyList<DailyUsage> days, DateOnly? highlightStart = null, DateOnly? highlightEnd = null)
    {
        _days = days;
        _highlightStart = highlightStart;
        _highlightEnd = highlightEnd;
        Render();
    }

    /// <summary>只更新与主统计范围重合的高亮，不重新查询数据。</summary>
    public void SetHighlight(DateOnly? start, DateOnly? end)
    {
        _highlightStart = start;
        _highlightEnd = end;
        _barLayer.HighlightStart = start;
        _barLayer.HighlightEnd = end;
        _barLayer.InvalidateVisual();

        // 悬停提示里也写了「是否属于主统计范围」，只重画柱体会让这行文案过时。
        for (var index = 0; index < _hitLayer.Children.Count && index < _days.Count; index++)
        {
            if (_hitLayer.Children[index] is Border hit)
            {
                hit.ToolTip = FormatTooltip(_days[index]);
            }
        }
    }

    private void Render()
    {
        _hitLayer.Children.Clear();
        _emptyLabel.Visibility = _days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_days.Count == 0)
        {
            _startLabel.Text = string.Empty;
            _endLabel.Text = string.Empty;
            _barLayer.Days = [];
            _barLayer.InvalidateVisual();
            return;
        }

        _startLabel.Text = _days[0].Date.ToString("MM-dd");
        _endLabel.Text = _days[^1].Date.ToString("MM-dd");

        _barLayer.Days = _days;
        _barLayer.HighlightStart = _highlightStart;
        _barLayer.HighlightEnd = _highlightEnd;
        _barLayer.InvalidateVisual();

        // 透明覆盖层只负责悬停提示，柱子本身由上层绘制，避免 24×N 个 Border 拖慢布局。
        _hitLayer.Columns = _days.Count;
        foreach (var day in _days)
        {
            var hit = new Border
            {
                Background = Brushes.Transparent,
                ToolTip = FormatTooltip(day),
            };
            ToolTipService.SetInitialShowDelay(hit, 350);
            ToolTipService.SetBetweenShowDelay(hit, 100);
            _hitLayer.Children.Add(hit);
        }
    }

    private string FormatTooltip(DailyUsage day)
    {
        var weekday = day.Date.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日",
        };

        var level = day.IsFire ? "火热日" : day.IsActive ? "活跃日" : "普通日";
        var peak = day.PeakHour >= 0
            ? $"时段高峰：{day.PeakHour:00}:00—{day.PeakHour + 1:00}:00"
            : "时段高峰：—";
        var highlighted = _highlightStart is { } start && _highlightEnd is { } end &&
                          day.Date >= start && day.Date <= end;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{day.Date:yyyy-MM-dd} {weekday}\n输入：{day.TotalCount:N0} 次\n使用时长：{day.ActiveMinuteCount} 分钟\n{peak}\n等级：{level}{(highlighted ? "\n（属于主统计范围）" : string.Empty)}");
    }

    private void HookTheme()
    {
        if (_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged += OnThemeChanged;
        _themeHooked = true;
    }

    private void UnhookTheme()
    {
        if (!_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged -= OnThemeChanged;
        _themeHooked = false;
    }

    private void OnThemeChanged() => _barLayer.InvalidateVisual();

    /// <summary>柱体绘制层：一天一根，柱内按小时堆叠。</summary>
    private sealed class BarLayer : FrameworkElement
    {
        public IReadOnlyList<DailyUsage> Days { get; set; } = [];

        public DateOnly? HighlightStart { get; set; }

        public DateOnly? HighlightEnd { get; set; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0 || Days.Count == 0)
            {
                return;
            }

            var columnWidth = width / Days.Count;
            var barWidth = Math.Clamp(columnWidth * 0.55, 2, 18);
            var maximum = Days.Max(day => day.TotalCount);
            var highlightBrush = new SolidColorBrush(Color.FromArgb(HighlightAlpha, 91, 156, 255));
            var inactiveBrush = new SolidColorBrush(ControlTheme.ActivityInactive);
            var hourBrushes = HourColors.Select(color => new SolidColorBrush(color)).ToArray();

            for (var index = 0; index < Days.Count; index++)
            {
                var day = Days[index];
                var columnX = index * columnWidth;
                var barX = columnX + ((columnWidth - barWidth) / 2);

                if (HighlightStart is { } start && HighlightEnd is { } end &&
                    day.Date >= start && day.Date <= end)
                {
                    drawingContext.DrawRoundedRectangle(
                        highlightBrush,
                        null,
                        new Rect(columnX + 1, 0, Math.Max(0, columnWidth - 2), height),
                        4,
                        4);
                }

                if (day.TotalCount == 0 || maximum == 0)
                {
                    drawingContext.DrawRoundedRectangle(
                        inactiveBrush,
                        null,
                        new Rect(barX, height - MinimumBarHeight, barWidth, MinimumBarHeight),
                        1.5,
                        1.5);
                    continue;
                }

                var barHeight = MinimumBarHeight +
                                (Math.Sqrt(day.TotalCount / (double)maximum) * (height - MinimumBarHeight - 1));
                var top = height - barHeight;

                drawingContext.PushClip(new RectangleGeometry(new Rect(barX, top, barWidth, barHeight), 2.5, 2.5));
                var cursor = height;
                for (var hour = 0; hour < 24 && hour < day.HourCounts.Count; hour++)
                {
                    var count = day.HourCounts[hour];
                    if (count == 0)
                    {
                        continue;
                    }

                    var segmentHeight = barHeight * count / day.TotalCount;
                    cursor -= segmentHeight;
                    // 多画 0.5px，避免相邻色块之间出现缝隙。
                    drawingContext.DrawRectangle(
                        hourBrushes[hour],
                        null,
                        new Rect(barX, cursor, barWidth, segmentHeight + 0.5));
                }

                drawingContext.Pop();
            }
        }
    }
}
