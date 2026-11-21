using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Keyana.Core;

namespace Keyana.App.Controls;

/// <summary>
/// 设置页的「滚轮校准」面板：调手势窗口 + 旁路观测真实滚轮事件。
///
/// 为什么需要校准：手势动作数靠「静默多久算一次新动作」判定，而这个阈值跟设备
/// （机械滚轮 / 触控板 / 高精度滚轮）、手的习惯都有关系，写在代码里只能拍一个默认值。
/// 面板把真实事件旁路出来，让人一边滚一边看判定结果，自己找合适的窗口。
///
/// 旁路期间滚轮不进统计：判定照常跑（要显示结果），但计数与距离都不落库，
/// 这一段是「测量」而不是「记录」。
///
/// 全部用代码搭，配色走主题资源，换肤时由 WPF 自动跟随。
/// </summary>
public sealed class WheelCalibrationPanel : Grid
{
    private static readonly int[] PresetWindows = [50, 100, 150, 200, 300, 400, 500];

    private readonly Slider _slider;
    private readonly TextBlock _windowValueText;
    private readonly Button _toggleButton;
    private readonly TextBlock _toggleHintText;
    private readonly TextBox _dpiBox;
    private readonly TextBlock _directionText;
    private readonly TextBlock _gestureText;
    private readonly TextBlock _deltaText;
    private readonly TextBlock _estimateText;
    private readonly List<Button> _presetButtons = [];

    private long _deltaTotal;
    private int _upCount;
    private int _downCount;
    private bool _calibrating;
    private bool _syncing;
    private double _dpiScale = 1.0;

    public WheelCalibrationPanel()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

        // ---------- 左：窗口调节 ----------
        var left = new StackPanel();

        var sliderHeader = new Grid();
        sliderHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var windowLabel = new TextBlock { Text = "手势窗口", VerticalAlignment = VerticalAlignment.Center };
        windowLabel.SetResourceReference(StyleProperty, "SectionTitleText");
        sliderHeader.Children.Add(windowLabel);

        _windowValueText = new TextBlock
        {
            Text = $"{WheelGestureDetector.DefaultWindowMs} ms",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        _windowValueText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        _windowValueText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        SetColumn(_windowValueText, 1);
        sliderHeader.Children.Add(_windowValueText);
        left.Children.Add(sliderHeader);

        _slider = new Slider
        {
            Minimum = WheelGestureDetector.MinWindowMs,
            Maximum = WheelGestureDetector.MaxWindowMs,
            Value = WheelGestureDetector.DefaultWindowMs,
            TickFrequency = 10,
            SmallChange = 10,
            LargeChange = 50,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _slider.SetResourceReference(StyleProperty, "WheelSliderStyle");
        _slider.ValueChanged += Slider_ValueChanged;
        left.Children.Add(_slider);

        var presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var preset in PresetWindows)
        {
            var chip = new Button
            {
                Content = preset.ToString(CultureInfo.InvariantCulture),
                CommandParameter = preset,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(9, 3, 9, 3),
                Focusable = false,
            };
            chip.SetResourceReference(StyleProperty, "FilterChipButtonStyle");
            chip.Click += Preset_Click;
            presets.Children.Add(chip);
            _presetButtons.Add(chip);
        }

        left.Children.Add(presets);

        var windowHint = new TextBlock
        {
            Text = "越短越灵敏：滚轮停一下再滚就会算成新的动作；越长越保守：一次连续滑动更难被拆开。"
                   + "改动立即生效，但只影响之后的新事件，已经统计过的历史不会重算。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        windowHint.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        windowHint.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        left.Children.Add(windowHint);

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };

        _toggleButton = new Button
        {
            Content = "开始校准",
            Focusable = false,
            Padding = new Thickness(13, 6, 13, 6),
        };
        _toggleButton.SetResourceReference(StyleProperty, "FilterChipButtonStyle");
        _toggleButton.Click += Toggle_Click;
        actions.Children.Add(_toggleButton);

        var clearButton = new Button
        {
            Content = "清空校准计数",
            Focusable = false,
            Padding = new Thickness(13, 6, 13, 6),
        };
        clearButton.SetResourceReference(StyleProperty, "FilterChipButtonStyle");
        clearButton.Click += (_, _) => ClearCounts();
        actions.Children.Add(clearButton);
        left.Children.Add(actions);

        _toggleHintText = new TextBlock
        {
            Text = "校准期间滚轮只做测量：不计入手势次数，也不累加距离；键盘与鼠标按键照常统计。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _toggleHintText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        _toggleHintText.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        left.Children.Add(_toggleHintText);

        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16, 0, 0),
        };
        divider.SetResourceReference(BackgroundProperty, "DividerBrush");
        left.Children.Add(divider);

        var dpiRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        dpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dpiStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var dpiLabel = new TextBlock { Text = "距离系数" };
        dpiLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        dpiLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        dpiStack.Children.Add(dpiLabel);

        var dpiHint = new TextBlock
        {
            Text = "只参与「估算距离」的换算，不改变任何入库数值。默认按「一格 3 行、每行 20 DIP」估算；"
                   + "系统是「每次滚 5 行」之类可以按比例调大。屏幕缩放已由 DIP 单位抵消，不必再乘。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 12, 0),
        };
        dpiHint.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        dpiHint.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        dpiStack.Children.Add(dpiHint);
        dpiRow.Children.Add(dpiStack);

        _dpiBox = new TextBox
        {
            Text = "1.00",
            Width = 66,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };
        _dpiBox.LostFocus += (_, _) => CommitDpiBox();
        _dpiBox.KeyDown += DpiBox_KeyDown;
        SetColumn(_dpiBox, 1);
        dpiRow.Children.Add(_dpiBox);
        left.Children.Add(dpiRow);

        Children.Add(left);

        // ---------- 右：实时读数 ----------
        var tile = new Border { VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(StyleProperty, "InnerTileStyle");
        SetColumn(tile, 2);

        var live = new StackPanel();

        var directionLabel = Caption("当前方向");
        live.Children.Add(directionLabel);

        _directionText = new TextBlock
        {
            Text = "等待滚动",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0),
        };
        _directionText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeTitle");
        _directionText.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        live.Children.Add(_directionText);

        live.Children.Add(Caption("本次校准手势", top: 12));
        _gestureText = Value("上滑 0 · 下滑 0");
        live.Children.Add(_gestureText);

        live.Children.Add(Caption("原始 delta", top: 10));
        _deltaText = Value("0（0.0 格）");
        live.Children.Add(_deltaText);

        live.Children.Add(Caption("估算距离", top: 10));
        _estimateText = Value("0 cm");
        live.Children.Add(_estimateText);

        var estimateHint = new TextBlock
        {
            Text = "估算，依屏幕 DPI 与系统滚动设置而变",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        estimateHint.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        estimateHint.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        live.Children.Add(estimateHint);

        tile.Child = live;
        Children.Add(tile);

        UpdatePresetTags();
    }

    /// <summary>窗口值变化（用户拖动滑块或点预设）。</summary>
    public event EventHandler<int>? WindowChanged;

    /// <summary>校准开关切换。</summary>
    public event EventHandler<bool>? CalibrationChanged;

    /// <summary>DPI 系数被用户改动。</summary>
    public event EventHandler<double>? DpiScaleChanged;

    public bool IsCalibrating => _calibrating;

    /// <summary>程序回填用：不触发 <see cref="WindowChanged"/>。</summary>
    public void SetWindowMs(int windowMs)
    {
        var clamped = WheelGestureDetector.ClampWindow(windowMs);
        _syncing = true;
        try
        {
            _slider.Value = clamped;
            _windowValueText.Text = $"{clamped} ms";
        }
        finally
        {
            _syncing = false;
        }

        UpdatePresetTags();
    }

    /// <summary>程序回填用：不触发 <see cref="DpiScaleChanged"/>。</summary>
    public void SetDpiScale(double scale)
    {
        _dpiScale = double.IsFinite(scale) && scale > 0 ? scale : 1.0;
        _dpiBox.Text = _dpiScale.ToString("0.00", CultureInfo.CurrentCulture);
        UpdateLiveValues();
    }

    /// <summary>
    /// 旁路事件入口。Raw Input 在窗口消息线程上调用；万一换成别的线程，
    /// 这里兜底切回 UI 线程，避免跨线程改可视元素。
    /// </summary>
    public void Observe(WheelInputInfo info)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Observe(info));
            return;
        }

        if (!_calibrating)
        {
            return;
        }

        var isUp = info.Direction == WheelDirection.Up;
        _deltaTotal += WheelMetrics.Magnitude(info.Delta);
        if (info.IsNewGesture)
        {
            if (isUp)
            {
                _upCount++;
            }
            else
            {
                _downCount++;
            }
        }

        _directionText.Text = isUp ? "↑ 上滑" : "↓ 下滑";
        _directionText.SetResourceReference(
            TextBlock.ForegroundProperty,
            info.IsNewGesture ? "AccentBrush" : "SecondaryTextBrush");
        UpdateLiveValues();
    }

    public void ClearCounts()
    {
        _deltaTotal = 0;
        _upCount = 0;
        _downCount = 0;
        _directionText.Text = "等待滚动";
        _directionText.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        UpdateLiveValues();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        var value = (int)Math.Round(eventArgs.NewValue);
        _windowValueText.Text = $"{value} ms";
        UpdatePresetTags();

        if (_syncing)
        {
            return;
        }

        WindowChanged?.Invoke(this, value);
    }

    private void Preset_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: int preset })
        {
            _slider.Value = preset;
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs eventArgs) =>
        SetCalibrating(!_calibrating);

    private void SetCalibrating(bool calibrating)
    {
        _calibrating = calibrating;
        _toggleButton.Content = calibrating ? "停止校准" : "开始校准";
        _toggleButton.Tag = calibrating ? "Active" : null;
        _toggleHintText.Text = calibrating
            ? "校准中：滚轮事件只在这里显示，不进入统计。停止后从下一个事件重新开始判定。"
            : "校准期间滚轮只做测量：不计入手势次数，也不累加距离；键盘与鼠标按键照常统计。";
        _toggleHintText.SetResourceReference(
            TextBlock.ForegroundProperty,
            calibrating ? "AccentBrush" : "TertiaryTextBrush");

        ClearCounts();
        CalibrationChanged?.Invoke(this, calibrating);
    }

    private void DpiBox_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            CommitDpiBox();
            eventArgs.Handled = true;
        }
    }

    private void CommitDpiBox()
    {
        if (double.TryParse(_dpiBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
            double.IsFinite(parsed) && parsed is >= 0.1 and <= 10)
        {
            if (Math.Abs(parsed - _dpiScale) > 0.001)
            {
                _dpiScale = parsed;
                DpiScaleChanged?.Invoke(this, parsed);
            }
        }

        // 合法值统一回写成两位小数；非法输入直接弹回当前值，不留下一个读不懂的字符串。
        _dpiBox.Text = _dpiScale.ToString("0.00", CultureInfo.CurrentCulture);
        UpdateLiveValues();
    }

    private void UpdateLiveValues()
    {
        _gestureText.Text = $"上滑 {_upCount:N0} · 下滑 {_downCount:N0}";
        _deltaText.Text = $"{_deltaTotal:N0}（{WheelMetrics.Notches(_deltaTotal):N1} 格）";

        // 系统缩放不参与换算：DIP 本来就是 DPI 无关的单位，乘或除都是重复计算。
        _estimateText.Text = FormatLength(WheelMetrics.EstimateCentimeters(_deltaTotal, _dpiScale));
    }

    private static string FormatLength(double centimeters) => centimeters switch
    {
        <= 0 => "0 cm",
        < 1 => $"{centimeters * 10:F1} mm",
        < 100 => $"{centimeters:F1} cm",
        _ => $"{centimeters / 100:F2} m",
    };

    private void UpdatePresetTags()
    {
        foreach (var chip in _presetButtons)
        {
            if (chip.CommandParameter is not int preset)
            {
                continue;
            }

            chip.Tag = Math.Abs(_slider.Value - preset) < 0.5 ? "Active" : null;
        }
    }

    private static TextBlock Caption(string text, double top = 0)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, top, 0, 0) };
        block.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        block.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        return block;
    }

    private static TextBlock Value(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        };
        block.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        return block;
    }
}
