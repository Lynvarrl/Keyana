using System.Windows.Media;

namespace KeyStats.App.Controls;

/// <summary>
/// 键盘热力图与时段热力图共用的热力配色，保证两处使用完全相同的颜色映射，
/// 并随同一个“配色”选项一起切换，避免只改键盘热力图而时段热力图不同步。
/// </summary>
public static class HeatPaletteColors
{
    /// <summary>无数据时的统一近白底色（键盘键帽与时段分段一致）。</summary>
    public static readonly Color EmptyColor = Color.FromRgb(247, 248, 250);

    private static readonly (double Position, Color Color)[] BlueMagentaStops =
    [
        // 蓝饱和渐变 + 品红：白→浅蓝→蓝→蓝紫→品红→玫红
        (0.00, Color.FromRgb(247, 248, 250)),  // 近白
        (0.15, Color.FromRgb(200, 220, 245)),  // 极浅蓝
        (0.30, Color.FromRgb(140, 185, 235)),  // 浅蓝
        (0.50, Color.FromRgb(75, 140, 215)),   // 中蓝
        (0.70, Color.FromRgb(140, 100, 200)),  // 蓝紫
        (0.85, Color.FromRgb(195, 65, 165)),   // 品红
        (1.00, Color.FromRgb(215, 75, 140)),   // 玫红
    ];

    private static readonly (double Position, Color Color)[] GreenAmberStops =
    [
        // 绿饱和渐变 + 琥珀：白→浅绿→绿→黄绿→琥珀→橙
        (0.00, Color.FromRgb(247, 248, 250)),  // 近白
        (0.15, Color.FromRgb(200, 235, 205)),  // 极浅绿
        (0.30, Color.FromRgb(140, 210, 150)),  // 浅绿
        (0.50, Color.FromRgb(70, 170, 90)),    // 中绿
        (0.70, Color.FromRgb(140, 195, 70)),   // 黄绿
        (0.85, Color.FromRgb(210, 170, 55)),   // 琥珀
        (1.00, Color.FromRgb(225, 130, 50)),   // 橙
    ];

    /// <summary>按计数与归一化强度（0~1）取色；计数为 0 时返回统一空色。</summary>
    public static Color GetColor(HeatPalette palette, ulong count, double intensity)
    {
        if (count == 0)
        {
            return EmptyColor;
        }

        var stops = palette == HeatPalette.GreenAmber ? GreenAmberStops : BlueMagentaStops;
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
