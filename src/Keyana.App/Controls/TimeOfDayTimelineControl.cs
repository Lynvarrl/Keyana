using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Keyana.App.Theme;

namespace Keyana.App.Controls;

/// <summary>
/// 一天之内的时段分布条。
///
/// 数据只有一档精度（分钟），「每块几分钟」是纯展示选择：块越细，一天里的块越多，
/// 块宽就越窄——窄到不足一个设备像素时按「该像素覆盖的块取最亮」合并，宁可少画几块，
/// 也不要把峰值跳过（跳过会让人以为那段时间很闲）。
///
/// 整条一次 <see cref="FrameworkElement.OnRender"/> 自绘：既是 144 块与 1440 块都画得动的前提，
/// 也避免了用上百个 Border 平铺时每段边界落在小数像素上、相邻色块之间露背景的问题。
/// </summary>
public sealed class TimeOfDayTimelineControl : Grid
{
    /// <summary>10 分钟一档时的段数，也是主界面缓存数组的长度。</summary>
    public const int SegmentCount = 144;

    /// <summary>一天的总分钟数：所有粒度的共同分母。</summary>
    public const int MinuteCount = 1440;

    /// <summary>可选的「每块几分钟」。全部整除 60，块边界才落在整分、与整点对齐。</summary>
    public static readonly int[] BucketLadder = [1, 2, 5, 10, 15, 30, 60];

    public const int DefaultBucketMinutes = 10;

    private readonly TimelineStrip _strip = new();
    private IReadOnlyList<ulong> _lastCounts = new ulong[SegmentCount];
    private int _lastMinutesPerSample = 10;
    private int _lastBucketMinutes = DefaultBucketMinutes;
    private HeatPalette _lastPalette = HeatPalette.BlueMagenta;
    private bool _themeHooked;

    public TimeOfDayTimelineControl()
    {
        Height = 40;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });

        _strip.Counts = _lastCounts;
        _strip.MinutesPerSample = _lastMinutesPerSample;
        _strip.BucketMinutes = _lastBucketMinutes;
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

    /// <summary>默认口径：10 分钟采样、10 分钟一块。</summary>
    public void SetData(IReadOnlyList<ulong> counts, HeatPalette palette = HeatPalette.BlueMagenta) =>
        SetData(counts, 10, DefaultBucketMinutes, palette);

    /// <summary>
    /// 更新数据。
    /// </summary>
    /// <param name="counts">按时间顺序的计数；<paramref name="minutesPerSample"/> 表示每一项覆盖几分钟。</param>
    /// <param name="minutesPerSample">每一项覆盖的分钟数：10（十分钟汇总）或 1（逐分钟）。</param>
    /// <param name="bucketMinutes">渲染时每块几分钟，必须能由采样精度整除，且整除 1440。</param>
    public void SetData(
        IReadOnlyList<ulong> counts,
        int minutesPerSample,
        int bucketMinutes,
        HeatPalette palette = HeatPalette.BlueMagenta)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (minutesPerSample is not (1 or 10))
        {
            throw new ArgumentOutOfRangeException(nameof(minutesPerSample), "采样精度只支持 1 或 10 分钟。");
        }

        if (bucketMinutes <= 0 || MinuteCount % bucketMinutes != 0 || bucketMinutes % minutesPerSample != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketMinutes),
                $"每块 {bucketMinutes} 分钟无法由 {minutesPerSample} 分钟采样精确聚合。");
        }

        var expected = MinuteCount / minutesPerSample;
        if (counts.Count != expected)
        {
            throw new ArgumentException($"时段数据长度应为 {expected}，实际 {counts.Count}。", nameof(counts));
        }

        _lastCounts = counts;
        _lastMinutesPerSample = minutesPerSample;
        _lastBucketMinutes = bucketMinutes;
        _lastPalette = palette;
        _strip.Counts = counts;
        _strip.MinutesPerSample = minutesPerSample;
        _strip.BucketMinutes = bucketMinutes;
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

    internal static string FormatMinute(int minuteOfDay)
    {
        if (minuteOfDay >= 24 * 60)
        {
            return "24:00";
        }

        return $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";
    }

    /// <summary>整条带子的绘制与悬停：一次 OnRender 画完，不再有一百多个可视元素。</summary>
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

        public int MinutesPerSample { get; set; } = 10;

        public int BucketMinutes { get; set; } = DefaultBucketMinutes;

        public HeatPalette Palette { get; set; } = HeatPalette.BlueMagenta;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0 || Counts.Count == 0)
            {
                return;
            }

            var blocks = AggregateBlocks(out var blockCount);
            if (blockCount == 0)
            {
                return;
            }

            var maximum = 0UL;
            foreach (var block in blocks)
            {
                if (block > maximum)
                {
                    maximum = block;
                }
            }

            // 边界必须落在设备像素上。系统缩放 125% / 150% 时，整数 DIP 边界会落到小数设备像素，
            // 相邻两段各自反锯齿，边界上各留一点半透明、露出背景——这就是「中间一条细深色线」。
            var dpi = VisualTreeHelper.GetDpi(this);
            var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d;
            var widthInPixels = width * scaleX;

            // 块宽不足一个设备像素时，把它并进左边那一列并取两者的较亮值：
            // 直接跳过会让条带出现真正的空洞，取平均又会把尖峰抹平。
            var columns = new List<(double Left, double Right, double Intensity, bool HasData)>();
            for (var index = 0; index < blockCount; index++)
            {
                var leftPixel = Math.Round(index * widthInPixels / blockCount);
                var rightPixel = Math.Round((index + 1) * widthInPixels / blockCount);
                var count = blocks[index];
                var intensity = maximum == 0 ? 0d : Math.Sqrt(count / (double)maximum);

                if (rightPixel <= leftPixel)
                {
                    if (columns.Count > 0)
                    {
                        var last = columns[^1];
                        columns[^1] = (last.Left, last.Right, Math.Max(last.Intensity, intensity),
                            last.HasData || count > 0);
                    }

                    continue;
                }

                columns.Add((leftPixel / scaleX, rightPixel / scaleX, intensity, count > 0));
            }

            var guidelines = new GuidelineSet();
            foreach (var column in columns)
            {
                guidelines.GuidelinesX.Add(column.Left);
                guidelines.GuidelinesX.Add(column.Right);
            }

            drawingContext.PushGuidelineSet(guidelines);
            try
            {
                var brushes = new Dictionary<Color, SolidColorBrush>();
                foreach (var (left, right, intensity, hasData) in columns)
                {
                    // 0 次输入的段用「未活跃」底色而不是「无数据色」：后者几乎是卡片底色，
                    // 夹在彩色段中间就像一个缺口，看着还是「一条深色线」。
                    var color = hasData
                        ? HeatPaletteColors.GetColor(Palette, 1, intensity)
                        : ControlTheme.ActivityInactive;
                    if (!brushes.TryGetValue(color, out var brush))
                    {
                        brush = new SolidColorBrush(color);
                        brush.Freeze();
                        brushes[color] = brush;
                    }

                    drawingContext.DrawRectangle(brush, null, new Rect(left, 0, right - left, height));
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        /// <summary>把采样序列按「每块几分钟」聚合成块。</summary>
        private ulong[] AggregateBlocks(out int blockCount)
        {
            blockCount = TimeOfDayTimelineControl.MinuteCount / BucketMinutes;
            var blocks = new ulong[blockCount];
            var samplesPerBlock = BucketMinutes / MinutesPerSample;
            for (var block = 0; block < blockCount; block++)
            {
                var start = block * samplesPerBlock;
                var total = 0UL;
                for (var offset = 0; offset < samplesPerBlock && start + offset < Counts.Count; offset++)
                {
                    total = checked(total + Counts[start + offset]);
                }

                blocks[block] = total;
            }

            return blocks;
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);

            if (ActualWidth <= 0 || Counts.Count == 0)
            {
                return;
            }

            var position = eventArgs.GetPosition(this);
            var blockCount = TimeOfDayTimelineControl.MinuteCount / BucketMinutes;
            var index = (int)(position.X * blockCount / ActualWidth);
            index = Math.Clamp(index, 0, blockCount - 1);
            if (index == _hoverIndex)
            {
                return;
            }

            _hoverIndex = index;
            _toolTip.Content = DescribeBlock(index);
            _toolTip.PlacementTarget = this;
            if (!_toolTip.IsOpen)
            {
                _toolTip.IsOpen = true;
            }
        }

        private string DescribeBlock(int block)
        {
            var startMinute = block * BucketMinutes;
            var endMinute = Math.Min(TimeOfDayTimelineControl.MinuteCount, startMinute + BucketMinutes);
            ulong total = 0;
            var samplesPerBlock = BucketMinutes / MinutesPerSample;
            var firstSample = startMinute / MinutesPerSample;
            for (var offset = 0; offset < samplesPerBlock && firstSample + offset < Counts.Count; offset++)
            {
                total = checked(total + Counts[firstSample + offset]);
            }

            return $"{FormatMinute(startMinute)}—{FormatMinute(endMinute)}\n输入次数：{total:N0}";
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
