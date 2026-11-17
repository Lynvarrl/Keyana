using System.Windows.Media;

namespace Keyana.App.Controls;

/// <summary>
/// 键盘热力图与时段热力图共用的热力配色，保证两处使用完全相同的颜色映射，
/// 并随同一个“配色”选项一起切换，避免只改键盘热力图而时段热力图不同步。
///
/// 深色主题不能直接复用浅色色阶：起点是近白色，放到深色背景上会像一排亮着的灯。
/// 因此每个配色都有一份深色版本的色阶，起点压到卡片底色附近。
/// </summary>
public static class HeatPaletteColors
{
    private static readonly (double Position, Color Color)[] LightBlueMagentaStops =
    [
        // 蓝饱和渐变 + 品红：白→浅蓝→蓝→蓝紫→品红→玫红
        (0.00, Color.FromRgb(247, 248, 250)),
        (0.15, Color.FromRgb(200, 220, 245)),
        (0.30, Color.FromRgb(140, 185, 235)),
        (0.50, Color.FromRgb(75, 140, 215)),
        (0.70, Color.FromRgb(140, 100, 200)),
        (0.85, Color.FromRgb(195, 65, 165)),
        (1.00, Color.FromRgb(215, 75, 140)),
    ];

    private static readonly (double Position, Color Color)[] LightGreenAmberStops =
    [
        // 绿饱和渐变 + 琥珀：白→浅绿→绿→黄绿→琥珀→橙
        (0.00, Color.FromRgb(247, 248, 250)),
        (0.15, Color.FromRgb(200, 235, 205)),
        (0.30, Color.FromRgb(140, 210, 150)),
        (0.50, Color.FromRgb(70, 170, 90)),
        (0.70, Color.FromRgb(140, 195, 70)),
        (0.85, Color.FromRgb(210, 170, 55)),
        (1.00, Color.FromRgb(225, 130, 50)),
    ];

    private static readonly (double Position, Color Color)[] DarkBlueMagentaStops =
    [
        // 深色版：从卡片底色抬起来，中段才出现明显的蓝，末端偏亮以保证在暗背景上有层次
        (0.00, Color.FromRgb(28, 34, 43)),
        (0.15, Color.FromRgb(26, 40, 61)),
        (0.30, Color.FromRgb(30, 63, 105)),
        (0.50, Color.FromRgb(44, 102, 176)),
        (0.70, Color.FromRgb(98, 86, 190)),
        (0.85, Color.FromRgb(176, 70, 158)),
        (1.00, Color.FromRgb(224, 106, 152)),
    ];

    private static readonly (double Position, Color Color)[] DarkGreenAmberStops =
    [
        (0.00, Color.FromRgb(28, 34, 43)),
        (0.15, Color.FromRgb(24, 45, 36)),
        (0.30, Color.FromRgb(28, 74, 51)),
        (0.50, Color.FromRgb(48, 122, 76)),
        (0.70, Color.FromRgb(110, 150, 58)),
        (0.85, Color.FromRgb(178, 138, 48)),
        (1.00, Color.FromRgb(216, 132, 58)),
    ];

    private static bool _isDark;

    public static void ApplyTheme(bool isDark) => _isDark = isDark;

    /// <summary>无数据时的统一底色（键盘键帽与时段分段一致）。</summary>
    public static Color EmptyColor => ControlTheme.EmptyCell;

    /// <summary>按计数与归一化强度（0~1）取色；计数为 0 时返回统一空色。</summary>
    public static Color GetColor(HeatPalette palette, ulong count, double intensity)
    {
        if (count == 0)
        {
            return EmptyColor;
        }

        var stops = (palette, _isDark) switch
        {
            (HeatPalette.GreenAmber, true) => DarkGreenAmberStops,
            (HeatPalette.GreenAmber, false) => LightGreenAmberStops,
            (_, true) => DarkBlueMagentaStops,
            _ => LightBlueMagentaStops,
        };

        return InterpolateStops(stops, intensity);
    }

    private static Color InterpolateStops((double Position, Color Color)[] stops, double intensity)
    {
        if (intensity <= 0) return stops[0].Color;
        if (intensity >= 1) return stops[^1].Color;

        for (var i = 0; i < stops.Length - 1; i++)
        {
            if (intensity <= stops[i + 1].Position)
            {
                var localT = (intensity - stops[i].Position) / (stops[i + 1].Position - stops[i].Position);
                return Interpolate(stops[i].Color, stops[i + 1].Color, localT);
            }
        }

        return stops[^1].Color;
    }

    private static Color Interpolate(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(start.R + ((end.R - start.R) * amount)),
            (byte)Math.Round(start.G + ((end.G - start.G) * amount)),
            (byte)Math.Round(start.B + ((end.B - start.B) * amount)));
    }
}
