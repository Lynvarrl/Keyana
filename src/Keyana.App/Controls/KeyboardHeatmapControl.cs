using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Keyana.App.Theme;
using Keyana.Core;

namespace Keyana.App.Controls;

public enum HeatScaleMode
{
    SquareRoot,
    Linear,
}

public enum HeatPalette
{
    BlueMagenta,
    GreenAmber,
}

public sealed class KeyboardHeatmapControl : Viewbox
{
    private const double Unit = 48;
    private const double Gap = 4;
    private const double Step = Unit + Gap;

    /// <summary>鼠标一行与键盘之间留小半格，肉眼能看出是两组，又不会让画布变高太多。</summary>
    private const double MouseRowGap = 0.45;

    private readonly Dictionary<KeyId, KeyCapVisual> _keyCaps = [];
    private IReadOnlyList<ulong> _lastCounts = new ulong[KeyCatalog.Count];
    private IReadOnlySet<KeyId> _lastPressed = new HashSet<KeyId>();
    private HeatScaleMode _lastScaleMode = HeatScaleMode.SquareRoot;
    private HeatPalette _lastPalette = HeatPalette.BlueMagenta;
    private ColorBasis _lastKeyboardBasis = ColorBasis.KeyboardOnly;
    private ColorBasis _lastMouseBasis = ColorBasis.KeyboardAndMouse;
    private KeyLayout _layout = KeyLayouts.Default;
    private bool _themeHooked;

    public KeyboardHeatmapControl()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        Rebuild();

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
    }

    /// <summary>当前配列。换配列只影响画法，不影响任何计数。</summary>
    public KeyLayout Layout => _layout;

    /// <summary>换一套配列重画。键帽集合整体重建，计数与按下状态沿用上一次的数据。</summary>
    public void SetLayout(KeyLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.Equals(layout.Id, _layout.Id, StringComparison.Ordinal) &&
            layout.Caps.Count == _layout.Caps.Count)
        {
            _layout = layout;
            return;
        }

        _layout = layout;
        Rebuild();
        Render();
    }

    public void SetData(
        IReadOnlyList<ulong> counts,
        IReadOnlySet<KeyId> pressedKeys,
        HeatScaleMode scaleMode,
        HeatPalette palette = HeatPalette.BlueMagenta,
        ColorBasis keyboardBasis = ColorBasis.KeyboardOnly,
        ColorBasis mouseBasis = ColorBasis.KeyboardAndMouse)
    {
        if (counts.Count != KeyCatalog.Count)
        {
            throw new ArgumentException("热力图计数向量长度不正确。", nameof(counts));
        }

        _lastCounts = counts;
        _lastPressed = pressedKeys;
        _lastScaleMode = scaleMode;
        _lastPalette = palette;
        _lastKeyboardBasis = keyboardBasis;
        _lastMouseBasis = mouseBasis;
        Render();
    }

    private void Render()
    {
        var counts = _lastCounts;
        var pressedKeys = _lastPressed;
        var scaleMode = _lastScaleMode;
        var palette = _lastPalette;

        var keyboardDenominator = HeatBasis.Denominator(counts, _lastKeyboardBasis);
        var mouseDenominator = HeatBasis.Denominator(counts, _lastMouseBasis);
        var total = counts.Aggregate(0UL, (sum, count) => checked(sum + count));
        var ranks = counts
            .Select((count, index) => (Count: count, Key: (KeyId)index))
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => (int)item.Key)
            .Select((item, index) => (item.Key, Rank: index + 1))
            .ToDictionary(item => item.Key, item => item.Rank);

        var weakText = new SolidColorBrush(ControlTheme.WeakText);
        var emptyText = new SolidColorBrush(ControlTheme.EmptyLabel);
        var strongText = new SolidColorBrush(ControlTheme.StrongText);
        var cellBorder = new SolidColorBrush(ControlTheme.CellBorder);
        var pressedBorder = new SolidColorBrush(ControlTheme.PressedBorder);

        foreach (var (keyId, visual) in _keyCaps)
        {
            var count = counts[(int)keyId];
            // 键帽和鼠标各按自己的基准着色：谁也不必迁就对方的量级。
            var denominator = HeatBasis.IsMouseKey(keyId) ? mouseDenominator : keyboardDenominator;
            var intensity = HeatBasis.Intensity(count, denominator);
            if (scaleMode == HeatScaleMode.SquareRoot)
            {
                intensity = Math.Sqrt(intensity);
            }

            var isPressed = pressedKeys.Contains(keyId);
            visual.Border.Background = new SolidColorBrush(HeatPaletteColors.GetColor(palette, count, intensity));
            visual.Border.BorderBrush = isPressed ? pressedBorder : cellBorder;
            visual.Border.BorderThickness = isPressed ? new Thickness(2.5) : new Thickness(1);
            visual.CountText.Text = count.ToString("N0");

            var useStrongText = intensity >= 0.72;
            visual.CountText.Foreground = useStrongText ? strongText : count == 0 ? emptyText : weakText;
            visual.NameText.Foreground = useStrongText ? strongText : count == 0 ? emptyText : weakText;

            var percentage = total == 0 ? 0d : count * 100d / total;
            var rankText = ranks.TryGetValue(keyId, out var rank) ? rank.ToString() : "—";
            // 着色基准直接写进提示：换基准后颜色变了，用户得能看出「变淡」是基准造成的。
            var basisText = denominator == 0
                ? "着色基准：暂无数据"
                : $"着色基准：{denominator:N0} 次（本键占 {count * 100d / denominator:F1}%）";
            visual.Border.ToolTip =
                $"{KeyCatalog.Get(keyId).DisplayName}\n次数：{count:N0}\n占全部输入：{percentage:F2}%\n排名：{rankText}\n{basisText}";
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

    private void OnThemeChanged() => Render();

    /// <summary>
    /// 按当前配列重建画布。1U = <see cref="Unit"/> 像素，整体由 Viewbox 等比缩放到卡片宽度，
    /// 所以配列只需要给出相对坐标。
    /// </summary>
    private void Rebuild()
    {
        _keyCaps.Clear();

        var mouseY = _layout.Rows + MouseRowGap;
        var canvas = new Canvas
        {
            Width = _layout.Columns * Step,
            Height = ((mouseY + 1) * Step) + 6,
            Background = Brushes.Transparent,
        };

        foreach (var cap in _layout.Caps)
        {
            Add(canvas, cap.Id, cap.DisplayLabel, cap.X, cap.Y, cap.Width, cap.Height);
        }

        AddMouseRow(canvas, mouseY);
        Child = canvas;
    }

    /// <summary>
    /// 鼠标一行：左 / 中 / 右、侧键 1 / 2、滚轮上 / 下，样式与键帽完全一致。
    ///
    /// 不画鼠标轮廓：数据只有 7 个按钮，形状本身不承载信息，反而会把卡片撑高；
    /// 用同样的键帽排一行，颜色、悬停提示、按下高亮都自动与键盘保持一致。
    /// </summary>
    private void AddMouseRow(Canvas canvas, double y)
    {
        var label = new TextBlock
        {
            Text = "鼠标",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ControlTheme.WeakText),
        };
        Canvas.SetLeft(label, 0);
        // 与键帽文字行垂直居中对齐，不靠猜像素：单元高减去一行小字的高度后对半分。
        Canvas.SetTop(label, (y * Step) + ((Unit - 15) / 2));
        canvas.Children.Add(label);

        const double mouseWidth = 1.5;
        var x = 2d;
        foreach (var id in new[]
                 {
                     KeyId.MouseLeftButton, KeyId.MouseMiddleButton, KeyId.MouseRightButton,
                     KeyId.MouseXButton1, KeyId.MouseXButton2,
                     KeyId.MouseWheelUp, KeyId.MouseWheelDown,
                 })
        {
            Add(canvas, id, KeyShortLabel.For(id), x, y, mouseWidth);
            x += mouseWidth;
        }
    }

    private void Add(Canvas canvas, KeyId keyId, string label, double x, double y, double width = 1, double height = 1)
    {
        var nameText = new TextBlock
        {
            Text = label,
            FontSize = width < 1.4 ? 10 : 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var countText = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.Children.Add(nameText);
        content.Children.Add(countText);

        var border = new Border
        {
            Width = (width * Step) - Gap,
            Height = (height * Step) - Gap,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(206, 214, 223)),
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
            Child = content,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(border, x * Step);
        Canvas.SetTop(border, y * Step);
        canvas.Children.Add(border);
        _keyCaps[keyId] = new KeyCapVisual(border, nameText, countText);
    }

    private sealed record KeyCapVisual(Border Border, TextBlock NameText, TextBlock CountText);
}
