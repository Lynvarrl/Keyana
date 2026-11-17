using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Keyana.App.Controls;

/// <summary>
/// 两位数时间输入：鼠标滚轮、上下按钮、方向键三种方式都能调。
///
/// 刻意没有做成可键入的文本框——滚轮场景下误触键盘输入反而容易把 12 改成 122，
/// 这里只做受控的逐格增减，最大值由构造参数决定。
/// </summary>
public sealed class TimeField : Grid
{
    private readonly TextBlock _valueText = new();
    private readonly int _maximum;
    private int _value;

    public TimeField(int maximum)
    {
        _maximum = maximum;
        Width = 58;
        Height = 30;
        Focusable = true;
        Cursor = Cursors.Hand;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });

        var background = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        background.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
        background.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        SetColumnSpan(background, 2);
        Children.Add(background);

        _valueText.HorizontalAlignment = HorizontalAlignment.Center;
        _valueText.VerticalAlignment = VerticalAlignment.Center;
        _valueText.FontWeight = FontWeights.SemiBold;
        _valueText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        _valueText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        _valueText.IsHitTestVisible = false;
        Children.Add(_valueText);

        var spinners = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 2, 2),
            IsHitTestVisible = true,
        };
        spinners.Children.Add(CreateSpinButton("\uE70E", +1));
        spinners.Children.Add(CreateSpinButton("\uE70D", -1));
        SetColumn(spinners, 1);
        Children.Add(spinners);

        MouseWheel += (_, eventArgs) =>
        {
            Value += eventArgs.Delta > 0 ? 1 : -1;
            eventArgs.Handled = true;
        };
        PreviewKeyDown += (_, eventArgs) =>
        {
            switch (eventArgs.Key)
            {
                case Key.Up:
                    Value++;
                    eventArgs.Handled = true;
                    break;
                case Key.Down:
                    Value--;
                    eventArgs.Handled = true;
                    break;
            }
        };

        UpdateText();
    }

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, _maximum);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetValueSilently(int value)
    {
        _value = Math.Clamp(value, 0, _maximum);
        UpdateText();
    }

    private Button CreateSpinButton(string glyph, int delta)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 13,
            Height = 12,
            Padding = new Thickness(0),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 6,
            Cursor = Cursors.Hand,
            Focusable = false,
            Style = Application.Current?.TryFindResource("GhostButtonStyle") as Style,
        };
        button.SetResourceReference(Control.ForegroundProperty, "SecondaryTextBrush");
        button.Click += (_, _) => Value += delta;
        return button;
    }

    private void UpdateText() => _valueText.Text = _value.ToString("00");
}
