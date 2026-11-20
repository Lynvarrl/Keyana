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
    private readonly Dictionary<KeyId, KeyCapVisual> _keyCaps = [];
    private IReadOnlyList<ulong> _lastCounts = new ulong[KeyCatalog.Count];
    private IReadOnlySet<KeyId> _lastPressed = new HashSet<KeyId>();
    private HeatScaleMode _lastScaleMode = HeatScaleMode.SquareRoot;
    private HeatPalette _lastPalette = HeatPalette.BlueMagenta;
    private ColorBasis _lastKeyboardBasis = ColorBasis.KeyboardOnly;
    private ColorBasis _lastMouseBasis = ColorBasis.KeyboardAndMouse;
    private bool _themeHooked;

    public KeyboardHeatmapControl()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        // 比键盘多一行：鼠标按键与滚轮按同样的键帽样式排在键盘下方，
        // 这样「鼠标没有热力图」不再是信息缺口，也不需要另开一张卡片。
        var canvas = new Canvas
        {
            Width = 24 * Step,
            Height = (7 * Step) + 6,
            Background = Brushes.Transparent,
        };
        Child = canvas;
        BuildKeyboard(canvas);
        BuildMouseRow(canvas);

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
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
            visual.Border.ToolTip = $"{KeyCatalog.Get(keyId).DisplayName}\n次数：{count:N0}\n占全部输入：{percentage:F2}%\n排名：{rankText}\n{basisText}";
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

    private void BuildKeyboard(Canvas canvas)
    {
        Add(canvas, KeyId.Escape, "Esc", 0, 0);
        AddSequence(canvas, 2, 0,
            K(KeyId.F1, "F1"), K(KeyId.F2, "F2"), K(KeyId.F3, "F3"), K(KeyId.F4, "F4"));
        AddSequence(canvas, 6.5, 0,
            K(KeyId.F5, "F5"), K(KeyId.F6, "F6"), K(KeyId.F7, "F7"), K(KeyId.F8, "F8"));
        AddSequence(canvas, 11, 0,
            K(KeyId.F9, "F9"), K(KeyId.F10, "F10"), K(KeyId.F11, "F11"), K(KeyId.F12, "F12"));
        AddSequence(canvas, 15.5, 0,
            K(KeyId.PrintScreen, "PrtSc"), K(KeyId.ScrollLock, "ScrLk"), K(KeyId.Pause, "Pause"));

        AddSequence(canvas, 0, 1,
            K(KeyId.Grave, "`"), K(KeyId.Digit1, "1"), K(KeyId.Digit2, "2"), K(KeyId.Digit3, "3"),
            K(KeyId.Digit4, "4"), K(KeyId.Digit5, "5"), K(KeyId.Digit6, "6"), K(KeyId.Digit7, "7"),
            K(KeyId.Digit8, "8"), K(KeyId.Digit9, "9"), K(KeyId.Digit0, "0"), K(KeyId.Minus, "-"),
            K(KeyId.Equal, "="), K(KeyId.Backspace, "Backspace", 2));
        AddSequence(canvas, 16, 1,
            K(KeyId.Insert, "Ins"), K(KeyId.Home, "Home"), K(KeyId.PageUp, "PgUp"));
        AddSequence(canvas, 20, 1,
            K(KeyId.NumLock, "Num"), K(KeyId.NumpadDivide, "/"), K(KeyId.NumpadMultiply, "*"), K(KeyId.NumpadSubtract, "-"));

        AddSequence(canvas, 0, 2,
            K(KeyId.Tab, "Tab", 1.5), K(KeyId.Q, "Q"), K(KeyId.W, "W"), K(KeyId.E, "E"), K(KeyId.R, "R"),
            K(KeyId.T, "T"), K(KeyId.Y, "Y"), K(KeyId.U, "U"), K(KeyId.I, "I"), K(KeyId.O, "O"), K(KeyId.P, "P"),
            K(KeyId.LeftBracket, "["), K(KeyId.RightBracket, "]"), K(KeyId.Backslash, "\\", 1.5));
        AddSequence(canvas, 16, 2,
            K(KeyId.Delete, "Del"), K(KeyId.End, "End"), K(KeyId.PageDown, "PgDn"));
        AddSequence(canvas, 20, 2,
            K(KeyId.Numpad7, "7"), K(KeyId.Numpad8, "8"), K(KeyId.Numpad9, "9"));
        Add(canvas, KeyId.NumpadAdd, "+", 23, 2, 1, 2);

        AddSequence(canvas, 0, 3,
            K(KeyId.CapsLock, "Caps", 1.75), K(KeyId.A, "A"), K(KeyId.S, "S"), K(KeyId.D, "D"), K(KeyId.F, "F"),
            K(KeyId.G, "G"), K(KeyId.H, "H"), K(KeyId.J, "J"), K(KeyId.K, "K"), K(KeyId.L, "L"),
            K(KeyId.Semicolon, ";"), K(KeyId.Apostrophe, "'"), K(KeyId.Enter, "Enter", 2.25));
        AddSequence(canvas, 20, 3,
            K(KeyId.Numpad4, "4"), K(KeyId.Numpad5, "5"), K(KeyId.Numpad6, "6"));

        AddSequence(canvas, 0, 4,
            K(KeyId.LeftShift, "Shift", 2.25), K(KeyId.Z, "Z"), K(KeyId.X, "X"), K(KeyId.C, "C"), K(KeyId.V, "V"),
            K(KeyId.B, "B"), K(KeyId.N, "N"), K(KeyId.M, "M"), K(KeyId.Comma, ","), K(KeyId.Period, "."),
            K(KeyId.Slash, "/"), K(KeyId.RightShift, "Shift", 2.75));
        Add(canvas, KeyId.ArrowUp, "↑", 17, 4);
        AddSequence(canvas, 20, 4,
            K(KeyId.Numpad1, "1"), K(KeyId.Numpad2, "2"), K(KeyId.Numpad3, "3"));
        Add(canvas, KeyId.NumpadEnter, "Enter", 23, 4, 1, 2);

        AddSequence(canvas, 0, 5,
            K(KeyId.LeftControl, "Ctrl", 1.25), K(KeyId.LeftWindows, "Win", 1.25), K(KeyId.LeftAlt, "Alt", 1.25),
            K(KeyId.Space, "Space", 6.25), K(KeyId.RightAlt, "Alt", 1.25), K(KeyId.RightWindows, "Win", 1.25),
            K(KeyId.Application, "Menu", 1.25), K(KeyId.RightControl, "Ctrl", 1.25));
        AddSequence(canvas, 16, 5,
            K(KeyId.ArrowLeft, "←"), K(KeyId.ArrowDown, "↓"), K(KeyId.ArrowRight, "→"));
        Add(canvas, KeyId.Numpad0, "0", 20, 5, 2);
        Add(canvas, KeyId.NumpadDecimal, ".", 22, 5);
    }

    /// <summary>
    /// 鼠标一行：左 / 中 / 右、侧键 1 / 2、滚轮上 / 下，样式与键帽完全一致。
    ///
    /// 不画鼠标轮廓：数据只有 7 个按钮，形状本身不承载信息，反而会把卡片撑高；
    /// 用同样的键帽排一行，颜色、悬停提示、按下高亮都自动与键盘保持一致。
    /// </summary>
    private void BuildMouseRow(Canvas canvas)
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
        Canvas.SetTop(label, (6 * Step) + ((Unit - 15) / 2));
        canvas.Children.Add(label);

        const double mouseWidth = 1.5;
        AddSequence(canvas, 2, 6,
            K(KeyId.MouseLeftButton, "左键", mouseWidth),
            K(KeyId.MouseMiddleButton, "中键", mouseWidth),
            K(KeyId.MouseRightButton, "右键", mouseWidth),
            K(KeyId.MouseXButton1, "侧键1", mouseWidth),
            K(KeyId.MouseXButton2, "侧键2", mouseWidth),
            K(KeyId.MouseWheelUp, "滚轮↑", mouseWidth),
            K(KeyId.MouseWheelDown, "滚轮↓", mouseWidth));
    }

    private void AddSequence(Canvas canvas, double startX, double y, params KeySpec[] keys)
    {
        var x = startX;
        foreach (var key in keys)
        {
            Add(canvas, key.Id, key.Label, x, y, key.Width);
            x += key.Width;
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

    private static KeySpec K(KeyId id, string label, double width = 1) => new(id, label, width);

    private sealed record KeyCapVisual(Border Border, TextBlock NameText, TextBlock CountText);

    private readonly record struct KeySpec(KeyId Id, string Label, double Width);
}
