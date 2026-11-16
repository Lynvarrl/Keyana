using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KeyStats.App.Theme;

namespace KeyStats.App.Controls;

public sealed class TenMinuteTimelineControl : Grid
{
    public const int SegmentCount = 144;
    private readonly Border[] _segmentVisuals = new Border[SegmentCount];
    private IReadOnlyList<ulong> _lastCounts = new ulong[SegmentCount];
    private HeatPalette _lastPalette = HeatPalette.BlueMagenta;
    private bool _themeHooked;

    public TenMinuteTimelineControl()
    {
        Height = 40;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });

        var segments = new UniformGrid { Columns = SegmentCount };
        for (var index = 0; index < SegmentCount; index++)
        {
            var startMinutes = index * 10;
            var endMinutes = (index + 1) * 10;
            var segment = new Border
            {
                Background = new SolidColorBrush(ControlTheme.EmptyCell),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(index % 6 == 0 ? 1 : 0, 0, 0, 0),
                ToolTip = $"{FormatTime(startMinutes)}—{FormatTime(endMinutes)}\n输入次数：0",
            };
            ToolTipService.SetInitialShowDelay(segment, 500);
            ToolTipService.SetBetweenShowDelay(segment, 100);
            ToolTipService.SetShowDuration(segment, 30_000);
            _segmentVisuals[index] = segment;
            segments.Children.Add(segment);
        }

        Children.Add(segments);

        var labels = new UniformGrid { Columns = 12 };
        SetRow(labels, 1);
        for (var hour = 0; hour < 24; hour += 2)
        {
            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-1, 2, 0, 0),
            };
            label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
            labels.Children.Add(label);
        }

        Children.Add(labels);

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
    }

    public void SetData(IReadOnlyList<ulong> counts, HeatPalette palette = HeatPalette.BlueMagenta)
    {
        if (counts.Count != SegmentCount)
        {
            throw new ArgumentException($"时段热力图需要 {SegmentCount} 个十分钟分段。", nameof(counts));
        }

        _lastCounts = counts;
        _lastPalette = palette;
        Render(palette);
    }

    private void Render(HeatPalette palette)
    {
        var counts = _lastCounts;
        var maximum = counts.DefaultIfEmpty().Max();
        var borderBrush = new SolidColorBrush(ControlTheme.CellBorder);

        for (var index = 0; index < SegmentCount; index++)
        {
            var count = counts[index];
            // 时段图固定使用平方根强度映射，颜色则与键盘热力图共用同一套配色，随选项联动。
            var intensity = maximum == 0 ? 0d : Math.Sqrt(count / (double)maximum);
            _segmentVisuals[index].Background = new SolidColorBrush(HeatPaletteColors.GetColor(palette, count, intensity));
            _segmentVisuals[index].BorderBrush = borderBrush;
            _segmentVisuals[index].ToolTip =
                $"{FormatTime(index * 10)}—{FormatTime((index + 1) * 10)}\n输入次数：{count:N0}";
        }
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

    private void OnThemeChanged() => Render(_lastPalette);

    private static string FormatTime(int totalMinutes)
    {
        if (totalMinutes == 24 * 60)
        {
            return "24:00";
        }

        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }
}
