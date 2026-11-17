using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Keyana.App.Theme;

namespace Keyana.App.Controls;

/// <summary>
/// 自绘月历：第一次点击确定起点，第二次点击确定终点。
///
/// 没有用 WPF 自带的 Calendar，原因有两个：SingleRange 模式的可视状态很难在深色主题下
/// 统一，且它的模板层级很深、每次换肤都要重写一大套 CalendarItem/CalendarDayButton。
/// 这里用 42 个 Border 自己画，配色和字号都能跟着主题走。
/// </summary>
public sealed class RangeCalendar : Grid
{
    private static readonly string[] WeekdayNames = ["一", "二", "三", "四", "五", "六", "日"];

    private readonly TextBlock _monthText = new();
    private readonly DayCell[] _cells = new DayCell[42];
    private DateOnly _displayMonth;
    private DateOnly? _anchor;
    private DateOnly? _rangeStart;
    private DateOnly? _rangeEnd;
    private DateOnly? _hoverDate;
    private bool _rangeComplete;
    private bool _themeHooked;

    private Brush _textPrimary = Brushes.Black;
    private Brush _textTertiary = Brushes.LightGray;
    private Brush _accent = Brushes.DodgerBlue;
    private Brush _accentSoft = Brushes.AliceBlue;
    private Brush _onAccent = Brushes.White;

    public RangeCalendar()
    {
        _displayMonth = FirstOfMonth(DateOnly.FromDateTime(DateTime.Today));
        Width = 308;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Children.Add(BuildHeader());
        Children.Add(BuildWeekdayRow());

        var days = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        for (var column = 0; column < 7; column++)
        {
            days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var row = 0; row < 6; row++)
        {
            days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var index = 0; index < _cells.Length; index++)
        {
            var cell = CreateDayCell();
            SetRow(cell.Root, index / 7);
            SetColumn(cell.Root, index % 7);
            days.Children.Add(cell.Root);
            _cells[index] = cell;
        }

        var daysHost = days;
        SetRow(daysHost, 2);
        Children.Add(daysHost);

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
        RenderMonth();
    }

    /// <summary>用户点完两个日期后触发。</summary>
    public event EventHandler? RangeSelected;

    public DateOnly? StartDate => _rangeStart;

    public DateOnly? EndDate => _rangeEnd;

    public void SetRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        _rangeStart = start;
        _rangeEnd = end;
        _rangeComplete = true;
        _anchor = null;
        _hoverDate = null;
        _displayMonth = FirstOfMonth(start);
        RenderMonth();
    }

    public void ShowMonth(DateOnly date)
    {
        _displayMonth = FirstOfMonth(date);
        RenderMonth();
    }

    private UIElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previous = CreateNavButton("\uE76B", () => ShowMonth(_displayMonth.AddMonths(-1)));
        header.Children.Add(previous);

        _monthText.HorizontalAlignment = HorizontalAlignment.Center;
        _monthText.VerticalAlignment = VerticalAlignment.Center;
        _monthText.FontWeight = FontWeights.SemiBold;
        _monthText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        SetColumn(_monthText, 1);
        header.Children.Add(_monthText);

        var next = CreateNavButton("\uE76C", () => ShowMonth(_displayMonth.AddMonths(1)));
        SetColumn(next, 2);
        header.Children.Add(next);

        return header;
    }

    private UIElement BuildWeekdayRow()
    {
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var name in WeekdayNames)
        {
            var label = new TextBlock
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
            weekdays.Children.Add(label);
        }

        SetRow(weekdays, 1);
        return weekdays;
    }

    private Button CreateNavButton(string glyph, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 28,
            Height = 28,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Style = Application.Current?.TryFindResource("SecondaryButtonStyle") as Style,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private DayCell CreateDayCell()
    {
        var text = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");

        var border = new Border
        {
            Margin = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Height = 34,
            Cursor = Cursors.Hand,
            Child = text,
        };

        DayCell? cell = null;
        border.MouseLeftButtonUp += (_, eventArgs) =>
        {
            if (cell is not null)
            {
                SelectDate(cell.Date);
                eventArgs.Handled = true;
            }
        };
        border.MouseEnter += (_, _) =>
        {
            if (cell is not null && _anchor is not null && !_rangeComplete)
            {
                _hoverDate = cell.Date;
                PaintCells();
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (_hoverDate is not null)
            {
                _hoverDate = null;
                PaintCells();
            }
        };

        cell = new DayCell(border, text);
        return cell;
    }

    private void SelectDate(DateOnly date)
    {
        if (_anchor is null || _rangeComplete)
        {
            _anchor = date;
            _rangeComplete = false;
            _rangeStart = date;
            _rangeEnd = null;
            _hoverDate = null;
        }
        else
        {
            var start = date < _anchor.Value ? date : _anchor.Value;
            var end = date < _anchor.Value ? _anchor.Value : date;
            _rangeStart = start;
            _rangeEnd = end;
            _anchor = null;
            _rangeComplete = true;
            _hoverDate = null;
            RangeSelected?.Invoke(this, EventArgs.Empty);
        }

        if (date.Month != _displayMonth.Month || date.Year != _displayMonth.Year)
        {
            _displayMonth = FirstOfMonth(date);
            RenderMonth();
        }
        else
        {
            PaintCells();
        }
    }

    private void RenderMonth()
    {
        _monthText.Text = $"{_displayMonth.Year} 年 {_displayMonth.Month} 月";

        var firstWeekdayOffset = ((int)_displayMonth.DayOfWeek + 6) % 7;
        var gridStart = _displayMonth.AddDays(-firstWeekdayOffset);

        for (var index = 0; index < _cells.Length; index++)
        {
            var date = gridStart.AddDays(index);
            _cells[index].Date = date;
            _cells[index].Text.Text = date.Day.ToString();
            _cells[index].IsCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
        }

        PaintCells();
    }

    private void PaintCells()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var previewRange = ResolvePreviewRange();

        foreach (var cell in _cells)
        {
            var isEndpoint = cell.Date == _rangeStart || cell.Date == _rangeEnd;
            var isInRange = previewRange is { } range && cell.Date >= range.Start && cell.Date <= range.End;
            var isToday = cell.Date == today;

            cell.Root.Background = isEndpoint
                ? _accent
                : isInRange
                    ? _accentSoft
                    : Brushes.Transparent;

            cell.Root.BorderBrush = isToday && !isEndpoint ? _accent : Brushes.Transparent;
            cell.Root.BorderThickness = new Thickness(isToday && !isEndpoint ? 1 : 0);
            cell.Root.Cursor = Cursors.Hand;

            cell.Text.Foreground = isEndpoint
                ? _onAccent
                : isInRange
                    ? _accent
                    : cell.IsCurrentMonth ? _textPrimary : _textTertiary;
            cell.Text.FontWeight = isEndpoint ? FontWeights.SemiBold : FontWeights.Normal;
            cell.Root.Opacity = cell.IsCurrentMonth || isInRange || isEndpoint ? 1 : 0.55;
        }
    }

    private (DateOnly Start, DateOnly End)? ResolvePreviewRange()
    {
        if (_rangeStart is { } start && _rangeEnd is { } end)
        {
            return (start, end);
        }

        if (_anchor is { } anchor)
        {
            var hover = _hoverDate ?? anchor;
            return hover < anchor ? (hover, anchor) : (anchor, hover);
        }

        return null;
    }

    private void HookTheme()
    {
        if (_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged += ApplyTheme;
        _themeHooked = true;
        ApplyTheme();
    }

    private void UnhookTheme()
    {
        if (!_themeHooked)
        {
            return;
        }

        ThemeManager.ThemeChanged -= ApplyTheme;
        _themeHooked = false;
    }

    private void ApplyTheme()
    {
        _textPrimary = Brush("PrimaryTextBrush", Brushes.Black);
        _textTertiary = Brush("TertiaryTextBrush", Brushes.LightGray);
        _accent = Brush("AccentBrush", Brushes.DodgerBlue);
        _accentSoft = Brush("AccentSoftBrush", Brushes.AliceBlue);
        _onAccent = Brush("OnAccentTextBrush", Brushes.White);
        PaintCells();
    }

    private static Brush Brush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private sealed class DayCell(Border root, TextBlock text)
    {
        public Border Root { get; } = root;

        public TextBlock Text { get; } = text;

        public DateOnly Date { get; set; }

        public bool IsCurrentMonth { get; set; }
    }
}
