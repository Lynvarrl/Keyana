using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Keyana.App.Theme;
using Keyana.Storage;

namespace Keyana.App.Controls;

/// <summary>
/// 每日活跃图：每根柱子是一天，柱高统一，纵向从 0 点排到 24 点，
/// 每一格代表当天的一个小时，颜色按输入量取热度色阶——相当于把「时段分布」压进每一列。
///
/// 柱高不再表达当天总量：总量差异交给颜色深浅，纵向只表达「这一天什么时候在用电脑」，
/// 于是每天都是一根等长的柱子，横向对比时段结构而不是总量高低。
/// 与主统计范围重合的日期会在柱子后面铺一层很淡的强调色，用来对齐两个区间。
/// </summary>
public sealed class DailyActivityControl : Grid
{
    private const double BarAreaHeight = 96;
    private const byte HighlightAlpha = 40;

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
    private HeatPalette _palette = HeatPalette.BlueMagenta;
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

        // 柱体不再用彩虹色区分小时，而是和「时段分布」共用同一套热度色阶：
        // 图例只需要说明纵向是什么、颜色深浅是什么。
        var legend = new TextBlock
        {
            Text = "上 = 0 点 · 下 = 24 点 · 颜色 = 该时段输入量",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        legend.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        legend.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");

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

    /// <summary>与「时段分布」共用同一个热度配色，换配色时两处一起变。</summary>
    public void SetPalette(HeatPalette palette)
    {
        _palette = palette;
        _barLayer.Palette = palette;
        _barLayer.InvalidateVisual();
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
        _barLayer.Palette = _palette;
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

    /// <summary>柱体绘制层：一天一根等长的柱子，纵向 24 格＝0 点到 24 点。</summary>
    private sealed class BarLayer : FrameworkElement
    {
        private const int HoursPerDay = 24;

        public IReadOnlyList<DailyUsage> Days { get; set; } = [];

        public DateOnly? HighlightStart { get; set; }

        public DateOnly? HighlightEnd { get; set; }

        public HeatPalette Palette { get; set; } = HeatPalette.BlueMagenta;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0 || Days.Count == 0)
            {
                return;
            }

            var columnWidth = width / Days.Count;
            var barWidth = Math.Clamp(columnWidth * 0.7, 2, 24);
            var rowHeight = height / HoursPerDay;

            // 所有柱子共用同一个量程：同一天里输入最多的那个小时最亮。
            // 若按每天各自归一，日子之间就没法比「什么时候更忙」了。
            var maximum = 0UL;
            foreach (var day in Days)
            {
                foreach (var count in day.HourCounts)
                {
                    if (count > maximum)
                    {
                        maximum = count;
                    }
                }
            }

            var highlightBrush = new SolidColorBrush(Color.FromArgb(HighlightAlpha, 91, 156, 255));
            var brushCache = new Dictionary<Color, SolidColorBrush>();

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

                drawingContext.PushClip(new RectangleGeometry(new Rect(barX, 0, barWidth, height), 2.5, 2.5));
                for (var hour = 0; hour < HoursPerDay; hour++)
                {
                    var count = hour < day.HourCounts.Count ? day.HourCounts[hour] : 0UL;
                    var intensity = maximum == 0 ? 0 : Math.Sqrt(count / (double)maximum);
                    var color = HeatPaletteColors.GetColor(Palette, count, intensity);
                    if (!brushCache.TryGetValue(color, out var brush))
                    {
                        brush = new SolidColorBrush(color);
                        brush.Freeze();
                        brushCache[color] = brush;
                    }

                    // 多画 0.5px，避免相邻两格之间出现缝隙。
                    drawingContext.DrawRectangle(
                        brush,
                        null,
                        new Rect(barX, hour * rowHeight, barWidth, rowHeight + 0.5));
                }

                drawingContext.Pop();
            }
        }
    }
}
