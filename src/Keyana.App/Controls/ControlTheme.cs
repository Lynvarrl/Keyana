using System.Windows.Media;

namespace Keyana.App.Controls;

/// <summary>
/// 自绘控件的取色板。
///
/// 这些控件在代码里画键帽和分段，没法直接吃 DynamicResource，所以由
/// <see cref="Theme.ThemeManager"/> 在换肤时把当前主题的颜色读出来放在这里，
/// 控件订阅 ThemeChanged 后重绘一次即可。
/// </summary>
public static class ControlTheme
{
    public static bool IsDark { get; private set; }

    public static Color EmptyCell { get; private set; } = Color.FromRgb(244, 246, 250);
    public static Color CellBorder { get; private set; } = Color.FromRgb(221, 228, 238);
    public static Color PressedBorder { get; private set; } = Color.FromRgb(47, 111, 208);
    public static Color EmptyLabel { get; private set; } = Color.FromRgb(107, 118, 136);
    public static Color WeakText { get; private set; } = Color.FromRgb(51, 64, 79);
    public static Color StrongText { get; private set; } = Color.FromRgb(255, 255, 255);
    public static Color TimelineLabel { get; private set; } = Color.FromRgb(138, 148, 164);
    public static Color ActivityInactive { get; private set; } = Color.FromRgb(230, 235, 243);
    public static Color ActivityActive { get; private set; } = Color.FromRgb(127, 174, 255);
    public static Color ActivityFire { get; private set; } = Color.FromRgb(242, 160, 61);

    public static void Apply(bool isDark)
    {
        IsDark = isDark;
        EmptyCell = Read("HeatEmptyColor", EmptyCell);
        CellBorder = Read("HeatCellBorderColor", CellBorder);
        PressedBorder = Read("HeatPressedBorderColor", PressedBorder);
        EmptyLabel = Read("HeatEmptyLabelColor", EmptyLabel);
        WeakText = Read("HeatWeakTextColor", WeakText);
        StrongText = Read("HeatStrongTextColor", StrongText);
        TimelineLabel = Read("TimelineLabelColor", TimelineLabel);
        ActivityInactive = Read("ActivityInactiveColor", ActivityInactive);
        ActivityActive = Read("ActivityActiveColor", ActivityActive);
        ActivityFire = Read("ActivityFireColor", ActivityFire);
    }

    private static Color Read(string key, Color fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) is Color color ? color : fallback;
}
