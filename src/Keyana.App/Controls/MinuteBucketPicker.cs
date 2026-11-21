using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Keyana.App.Controls;

/// <summary>
/// 「每块几分钟」的数字滚轮：左右箭头逐档切换，鼠标滚轮也能调。
///
/// 刻意不做上下限提示和自动纠正：选细了块会变窄，用户自己会看出来再调回来，
/// 替他拦着反而挡住了「我就想看一眼」的用法。
/// </summary>
public sealed class MinuteBucketPicker : Grid
{
    private readonly TextBlock _valueText;
    private int _value = TimeOfDayTimelineControl.DefaultBucketMinutes;

    public MinuteBucketPicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        VerticalAlignment = VerticalAlignment.Center;

        var previous = CreateArrow("\uE76B", "更细：每块的分钟数更少");
        previous.Click += (_, _) => Step(-1);
        Children.Add(previous);

        _valueText = new TextBlock
        {
            MinWidth = 78,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        _valueText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        SetColumn(_valueText, 1);
        Children.Add(_valueText);

        var next = CreateArrow("\uE76C", "更粗：每块的分钟数更多");
        next.Click += (_, _) => Step(1);
        SetColumn(next, 2);
        Children.Add(next);

        MouseWheel += OnMouseWheel;
        UpdateText();
    }

    public event EventHandler? ValueChanged;

    /// <summary>每块几分钟，只会取阶梯上的值。</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Nearest(value);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>滚到阶梯外时取最近的一档，而不是报错：设置文件被手改坏也能继续用。</summary>
    private static int Nearest(int value)
    {
        var ladder = TimeOfDayTimelineControl.BucketLadder;
        var best = ladder[0];
        var bestDistance = int.MaxValue;
        foreach (var step in ladder)
        {
            var distance = Math.Abs(step - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = step;
            }
        }

        return best;
    }

    private void Step(int direction)
    {
        var ladder = TimeOfDayTimelineControl.BucketLadder;
        var index = Array.IndexOf(ladder, _value);
        if (index < 0)
        {
            index = Array.IndexOf(ladder, TimeOfDayTimelineControl.DefaultBucketMinutes);
        }

        Value = ladder[Math.Clamp(index + direction, 0, ladder.Length - 1)];
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        Step(eventArgs.Delta > 0 ? -1 : 1);
        eventArgs.Handled = true;
    }

    private void UpdateText() => _valueText.Text = $"{_value} 分钟";

    private static Button CreateArrow(string glyph, string toolTip) => new()
    {
        Content = glyph,
        Width = 22,
        Height = 22,
        Padding = new Thickness(0),
        FontSize = 10,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        Focusable = false,
        Cursor = Cursors.Hand,
        ToolTip = toolTip,
        Style = Application.Current?.TryFindResource("GhostButtonStyle") as Style,
    };
}
