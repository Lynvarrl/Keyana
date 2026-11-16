using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyStats.App.Controls;

/// <summary>小时 + 分钟的组合输入，供日期区间选择器使用。</summary>
public sealed class TimeWheel : Grid
{
    private readonly TimeField _hours = new(23);
    private readonly TimeField _minutes = new(59);
    private bool _suppress;
    private int _hour;
    private int _minute;

    public TimeWheel()
    {
        VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Children.Add(_hours);

        var colon = new TextBlock
        {
            Text = ":",
            Margin = new Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        colon.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        SetColumn(colon, 1);
        Children.Add(colon);

        SetColumn(_minutes, 2);
        Children.Add(_minutes);

        _hours.ValueChanged += (_, _) => OnPartChanged();
        _minutes.ValueChanged += (_, _) => OnPartChanged();
        UpdateParts();
    }

    public event EventHandler? ValueChanged;

    public TimeSpan Value
    {
        get => new(_hour, _minute, 0);
        set
        {
            _hour = Math.Clamp(value.Hours, 0, 23);
            _minute = Math.Clamp(value.Minutes, 0, 59);
            UpdateParts();
        }
    }

    public void SetValueSilently(TimeSpan value)
    {
        _suppress = true;
        _hour = Math.Clamp(value.Hours, 0, 23);
        _minute = Math.Clamp(value.Minutes, 0, 59);
        UpdateParts();
        _suppress = false;
    }

    private void OnPartChanged()
    {
        if (_suppress)
        {
            return;
        }

        _hour = _hours.Value;
        _minute = _minutes.Value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateParts()
    {
        _hours.SetValueSilently(_hour);
        _minutes.SetValueSilently(_minute);
    }
}
