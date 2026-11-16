using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KeyStats.App.Theme;
using KeyStats.Storage;

namespace KeyStats.App.Controls;

/// <summary>
/// 每日活跃条：每根柱子是一天，颜色区分「普通 / 活跃（≥325）/ 火热（≥7999）」。
/// 高度按平方根压缩，避免某一天 2 万次输入把其它日子压成一条线。
/// </summary>
public sealed class DailyActivityControl : Grid
{
    private const double BarAreaHeight = 66;
    private const double MinimumBarHeight = 3;

    private readonly UniformGrid _bars = new() { Height = BarAreaHeight, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _startLabel = new();
    private readonly TextBlock _endLabel = new();
    private readonly TextBlock _emptyLabel = new()
    {
        Text = "所选区间暂无数据",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private IReadOnlyList<DailyUsage> _days = [];
    private bool _themeHooked;

    public DailyActivityControl()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarAreaHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        var barHost = new Grid();
        barHost.Children.Add(_bars);
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

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
        Render();
    }

    public void SetData(IReadOnlyList<DailyUsage> days)
    {
        _days = days;
        Render();
    }

    private void Render()
    {
        _bars.Children.Clear();
        _emptyLabel.Visibility = _days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_days.Count == 0)
        {
            _startLabel.Text = string.Empty;
            _endLabel.Text = string.Empty;
            return;
        }

        _startLabel.Text = _days[0].Date.ToString("MM-dd");
        _endLabel.Text = _days[^1].Date.ToString("MM-dd");

        var maximum = _days.Max(day => day.TotalCount);
        var muted = Blend(ControlTheme.ActivityActive, ControlTheme.ActivityInactive, 0.55);

        // 柱子给一个固定宽度：Border 没有内容，横向居中会让 DesiredSize 变成 0。
        // 按天数分档，既能保证一天时不会糊成一整条，也能让 30 天以上不互相重叠。
        var barWidth = _days.Count switch
        {
            <= 12 => 18,
            <= 30 => 10,
            <= 60 => 5,
            _ => 3,
        };

        foreach (var day in _days)
        {
            var color = day.TotalCount == 0
                ? ControlTheme.ActivityInactive
                : day.IsFire
                    ? ControlTheme.ActivityFire
                    : day.IsActive
                        ? ControlTheme.ActivityActive
                        : muted;

            var height = day.TotalCount == 0 || maximum == 0
                ? MinimumBarHeight
                : MinimumBarHeight + (Math.Sqrt(day.TotalCount / (double)maximum) * (BarAreaHeight - MinimumBarHeight - 2));

            var bar = new Border
            {
                Height = Math.Max(MinimumBarHeight, height),
                Width = barWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2.5),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = FormatTooltip(day),
            };
            ToolTipService.SetInitialShowDelay(bar, 350);
            _bars.Children.Add(bar);
        }

        _bars.Columns = _days.Count;
    }

    private void HookTheme()
    {
        if (_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged += Render;
        _themeHooked = true;
    }

    private void UnhookTheme()
    {
        if (!_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged -= Render;
        _themeHooked = false;
    }

    private static string FormatTooltip(DailyUsage day)
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
        return $"{day.Date:yyyy-MM-dd} {weekday}\n输入：{day.TotalCount:N0} 次\n使用时长：{day.ActiveMinuteCount} 分钟\n等级：{level}";
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }
}
