using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Keyana.App.Theme;

namespace Keyana.App.Controls;

/// <summary>
/// 一天 144 段（每段 10 分钟）的时段分布条。
///
/// 这里不再用 144 个 Border 拼：UniformGrid 把宽度分成 144 份后，每段的左右边界都落在
/// 小数像素上，相邻两段之间会露出背景，表现为「彩块中间一条细深色线」；再加上原先每 6 段
/// 还带一条 1px 的透明左边框（等于在条带上打了个洞），暗色主题下就变成规则的暗线。
/// 现在改成整条自绘：每段的左右边界都取整到物理像素，相邻两段共享同一条边界，既无缝也不糊。
/// </summary>
public sealed class TenMinuteTimelineControl : Grid
{
    public const int SegmentCount = 144;

    private readonly TimelineStrip _strip = new();
    private IReadOnlyList<ulong> _lastCounts = new ulong[SegmentCount];
    private HeatPalette _lastPalette = HeatPalette.BlueMagenta;
    private bool _themeHooked;

    public TenMinuteTimelineControl()
    {
        Height = 40;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });

        _strip.Counts = _lastCounts;
        Children.Add(_strip);

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

        Unloaded += (_, _) => _strip.CloseToolTip();
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
        _strip.Counts = counts;
        _strip.Palette = palette;
        _strip.InvalidateVisual();
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

    private void OnThemeChanged() => _strip.InvalidateVisual();

    internal static string FormatTime(int totalMinutes)
    {
        if (totalMinutes == 24 * 60)
        {
            return "24:00";
        }

        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    /// <summary>整条带子的绘制与悬停：一次 OnRender 画完 144 段，不再有一百多个可视元素。</summary>
    private sealed class TimelineStrip : FrameworkElement
    {
        private readonly ToolTip _toolTip = new()
        {
            Placement = PlacementMode.Mouse,
            StaysOpen = true,
        };

        private int _hoverIndex = -1;

        public TimelineStrip()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
        }

        public IReadOnlyList<ulong> Counts { get; set; } = new ulong[SegmentCount];

        public HeatPalette Palette { get; set; } = HeatPalette.BlueMagenta;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0 || Counts.Count == 0)
            {
                return;
            }

            var maximum = 0UL;
            foreach (var count in Counts)
            {
                if (count > maximum)
                {
                    maximum = count;
                }
            }

            var brushes = new Dictionary<Color, SolidColorBrush>();
            for (var index = 0; index < Counts.Count; index++)
            {
                // 关键两点：
                // 1) 左右边界都取整到物理像素，相邻两段共用同一条边界，不会露出背景；
                // 2) 段宽至少 1px，窗口很窄时宁可少显示几段也不画成虚线。
                var left = Math.Round(index * width / Counts.Count);
                var right = Math.Round((index + 1) * width / Counts.Count);
                if (right <= left)
                {
                    right = left + 1;
                }

                var count = Counts[index];
                // 时段图固定使用平方根强度映射，颜色与键盘热力图共用同一套配色。
                var intensity = maximum == 0 ? 0d : Math.Sqrt(count / (double)maximum);
                var color = HeatPaletteColors.GetColor(Palette, count, intensity);
                if (!brushes.TryGetValue(color, out var brush))
                {
                    brush = new SolidColorBrush(color);
                    brush.Freeze();
                    brushes[color] = brush;
                }

                drawingContext.DrawRectangle(brush, null, new Rect(left, 0, right - left, height));
            }
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);

            if (ActualWidth <= 0 || Counts.Count == 0)
            {
                return;
            }

            var position = eventArgs.GetPosition(this);
            var index = (int)(position.X * Counts.Count / ActualWidth);
            index = Math.Clamp(index, 0, Counts.Count - 1);
            if (index == _hoverIndex)
            {
                return;
            }

            _hoverIndex = index;
            _toolTip.Content =
                $"{FormatTime(index * 10)}—{FormatTime((index + 1) * 10)}\n输入次数：{Counts[index]:N0}";
            _toolTip.PlacementTarget = this;
            if (!_toolTip.IsOpen)
            {
                _toolTip.IsOpen = true;
            }
        }

        protected override void OnMouseLeave(MouseEventArgs eventArgs)
        {
            base.OnMouseLeave(eventArgs);
            CloseToolTip();
        }

        public void CloseToolTip()
        {
            _hoverIndex = -1;
            _toolTip.IsOpen = false;
        }
    }
}
