namespace Keyana.Core;

/// <summary>
/// 热力图的着色基准：决定「最亮的那个键」从哪一组里挑。
///
/// 键盘和鼠标可以各选一个基准——键盘只和键盘比，鼠标只和鼠标比，
/// 或者两者混在一起比。鼠标左键的次数通常是任何单个键的若干倍，
/// 直接用全局最大值归一，键盘会整体压成浅色，所以默认让两边各比各的。
/// </summary>
public enum ColorBasis
{
    /// <summary>只在这一组键内部取最大值。</summary>
    KeyboardOnly,

    /// <summary>只在鼠标按键与滚轮内部取最大值。</summary>
    MouseOnly,

    /// <summary>键盘与鼠标一起取最大值。</summary>
    KeyboardAndMouse,
}

/// <summary>
/// 着色基准的纯计算部分。
///
/// 放在 Core 而不是控件里，是为了能进回归测试：色阶是否符合预期不靠肉眼看截图，
/// 而是断言「同一份计数换基准后每个键的强度」。控件只负责把强度映射成颜色。
/// </summary>
public static class HeatBasis
{
    public static bool IsMouseKey(KeyId id) => id is
        KeyId.MouseLeftButton or KeyId.MouseRightButton or KeyId.MouseMiddleButton or
        KeyId.MouseXButton1 or KeyId.MouseXButton2 or
        KeyId.MouseWheelUp or KeyId.MouseWheelDown or
        KeyId.WheelGestureUp or KeyId.WheelGestureDown;

    /// <summary>按基准取分母：所涉范围内最大的单键计数。全为 0 时返回 0。</summary>
    public static ulong Denominator(IReadOnlyList<ulong> counts, ColorBasis basis)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var maximum = 0UL;
        for (var index = 0; index < counts.Count; index++)
        {
            var isMouse = IsMouseKey((KeyId)index);
            if (basis == ColorBasis.KeyboardOnly && isMouse)
            {
                continue;
            }

            if (basis == ColorBasis.MouseOnly && !isMouse)
            {
                continue;
            }

            if (counts[index] > maximum)
            {
                maximum = counts[index];
            }
        }

        return maximum;
    }

    /// <summary>线性强度 0～1；分母为 0 或计数为 0 时返回 0（显示为空色）。</summary>
    public static double Intensity(ulong count, ulong denominator) =>
        count == 0 || denominator == 0 ? 0d : Math.Min(1d, count / (double)denominator);

    /// <summary>某个键该用哪个基准着色：鼠标键用鼠标的，其余用键盘的。</summary>
    public static ColorBasis BasisFor(KeyId id, ColorBasis keyboardBasis, ColorBasis mouseBasis) =>
        IsMouseKey(id) ? mouseBasis : keyboardBasis;

    /// <summary>
    /// 解析设置文件里的名字。拼错或版本更早留下的空值一律降级为默认值，
    /// 不允许一个字符串把整个热力图的着色算坏。
    /// </summary>
    public static ColorBasis Parse(string? value, ColorBasis fallback) =>
        Enum.TryParse<ColorBasis>(value, ignoreCase: true, out var basis) ? basis : fallback;
}
