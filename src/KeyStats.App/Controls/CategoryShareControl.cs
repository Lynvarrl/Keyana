using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KeyStats.App.Theme;

namespace KeyStats.App.Controls;

/// <summary>分析页的类别占比环形图：左侧圆环，右侧图例。全部用代码画，便于跟随主题。</summary>
public sealed class CategoryShareControl : Grid
{
    private const double Size = 142;
    private const double StrokeThickness = 16;

    private static readonly Color[] LightColors =
    [
        Color.FromRgb(63, 127, 232),
        Color.FromRgb(34, 160, 107),
        Color.FromRgb(224, 145, 43),
        Color.FromRgb(142, 107, 232),
        Color.FromRgb(214, 90, 147),
        Color.FromRgb(122, 138, 160),
    ];

    private static readonly Color[] DarkColors =
    [
        Color.FromRgb(107, 166, 255),
        Color.FromRgb(74, 222, 155),
        Color.FromRgb(245, 183, 91),
        Color.FromRgb(167, 139, 250),
        Color.FromRgb(240, 125, 176),
        Color.FromRgb(148, 163, 184),
    ];

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly StackPanel _legend = new();
    private readonly TextBlock _totalText = new();
    private IReadOnlyList<CategorySlice> _data = [];
    private bool _themeHooked;

    public CategoryShareControl()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var donutHost = new Grid { Width = Size, Height = Size, VerticalAlignment = VerticalAlignment.Center };
        donutHost.Children.Add(_canvas);

        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _totalText.HorizontalAlignment = HorizontalAlignment.Center;
        _totalText.FontWeight = FontWeights.SemiBold;
        _totalText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSubtitle");
        _totalText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        center.Children.Add(_totalText);

        var caption = new TextBlock
        {
            Text = "总输入",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };
        caption.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
        center.Children.Add(caption);
        donutHost.Children.Add(center);

        Children.Add(donutHost);

        _legend.VerticalAlignment = VerticalAlignment.Center;
        _legend.Margin = new Thickness(18, 0, 0, 0);
        SetColumn(_legend, 1);
        Children.Add(_legend);

        Loaded += (_, _) => HookTheme();
        Unloaded += (_, _) => UnhookTheme();
        HookTheme();
        Render();
    }

    public void SetData(IReadOnlyList<CategorySlice> data)
    {
        _data = data;
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        _legend.Children.Clear();

        var total = _data.Aggregate(0UL, (sum, slice) => checked(sum + slice.Count));
        _totalText.Text = total.ToString("N0");

        var colors = ControlTheme.IsDark ? DarkColors : LightColors;
        var trackBrush = new SolidColorBrush(ControlTheme.EmptyCell);
        var center = new Point(Size / 2, Size / 2);
        var radius = (Size - StrokeThickness) / 2 - 1;

        _canvas.Children.Add(new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = trackBrush,
            StrokeThickness = StrokeThickness,
            Margin = new Thickness(StrokeThickness / 2 + 1),
        });

        if (total == 0)
        {
            var empty = new TextBlock
            {
                Text = "所选区间暂无输入",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            empty.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryTextBrush");
            _legend.Children.Add(empty);
            return;
        }

        var angle = -90d;
        for (var index = 0; index < _data.Count; index++)
        {
            var slice = _data[index];
            if (slice.Count == 0)
            {
                continue;
            }

            var brush = new SolidColorBrush(colors[index % colors.Length]);
            var sweep = slice.Count * 360d / total;

            if (sweep >= 359.99)
            {
                _canvas.Children.Add(new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Stroke = brush,
                    StrokeThickness = StrokeThickness,
                    Margin = new Thickness(StrokeThickness / 2 + 1),
                });
            }
            else
            {
                _canvas.Children.Add(new Path
                {
                    Data = BuildArc(center, radius, angle, sweep),
                    Stroke = brush,
                    StrokeThickness = StrokeThickness,
                    StrokeStartLineCap = PenLineCap.Flat,
                    StrokeEndLineCap = PenLineCap.Flat,
                });
            }

            AddLegendRow(slice.Category, slice.Count * 100d / total, brush);
            angle += sweep;
        }
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweep)
    {
        var startRadians = startAngle * Math.PI / 180d;
        var endRadians = (startAngle + sweep) * Math.PI / 180d;
        var startPoint = new Point(
            center.X + (radius * Math.Cos(startRadians)),
            center.Y + (radius * Math.Sin(startRadians)));
        var endPoint = new Point(
            center.X + (radius * Math.Cos(endRadians)),
            center.Y + (radius * Math.Sin(endRadians)));

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            0,
            sweep > 180,
            SweepDirection.Clockwise,
            true));
        return new PathGeometry([figure]);
    }

    private void AddLegendRow(string category, double share, Brush brush)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(3),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        row.Children.Add(dot);

        var name = new TextBlock { Text = category, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        SetColumn(name, 1);
        row.Children.Add(name);

        var percent = new TextBlock
        {
            Text = $"{share:F1}%",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        percent.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        percent.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        SetColumn(percent, 2);
        row.Children.Add(percent);

        _legend.Children.Add(row);
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
}

public readonly record struct CategorySlice(string Category, ulong Count);
