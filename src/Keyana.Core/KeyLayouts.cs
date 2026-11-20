namespace Keyana.Core;

/// <summary>
/// 配列的可读写法：按行、按起点堆键帽，坐标全部以 1U 为单位。
/// 手写一百多个坐标既难读又容易错位，这里把「这一行从 x 开始连续摆这些键」抽出来。
/// </summary>
public sealed class LayoutBuilder
{
    private readonly List<KeyCap> _caps = [];

    public LayoutBuilder Key(KeyId id, double x, double y, double width = 1, double height = 1, string? label = null)
    {
        _caps.Add(new KeyCap(id, x, y, width, height, label));
        return this;
    }

    /// <summary>从 <paramref name="x"/> 开始，把一串键按顺序摆在同一行。</summary>
    public LayoutBuilder Row(double x, double y, params (KeyId Id, double Width)[] keys)
    {
        foreach (var (id, width) in keys)
        {
            _caps.Add(new KeyCap(id, x, y, width));
            x += width;
        }

        return this;
    }

    public KeyLayout Build(string id, string name, double columns) => new(id, name, columns, _caps.ToArray());
}

/// <summary>
/// 内置配列。
///
/// 说明：104 与 87 是标准 ANSI 尺寸；84 / 68 / 60 是常见的 75% / 65% / 60%。
/// 「紧凑全尺寸」两套是在 104 的几何上按用户实际键盘去掉几颗键（PrtSc / ScrLk / Pause / Ins），
/// 命名按「有没有 PrtSc / Ins」而不是精确键数——真要逐键还原某款 98 键盘，
/// 用 Data\layouts 里的自定义 JSON 覆盖即可，不必改代码。
/// </summary>
public static class KeyLayouts
{
    public const string DefaultId = "ansi-104";

    private static readonly Lazy<IReadOnlyList<KeyLayout>> BuiltInLayouts = new(Build);

    public static IReadOnlyList<KeyLayout> BuiltIn => BuiltInLayouts.Value;

    public static KeyLayout Default => Find(DefaultId) ?? BuiltIn[0];

    public static KeyLayout? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : BuiltIn.FirstOrDefault(layout => string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<KeyLayout> Build()
    {
        var full = BuildFullSize();
        var withoutNumpad = StripNumpad(full);
        var compactFull = BuildCompactFullSize(full);

        return
        [
            full,
            compactFull.WithPrintedScreen,
            compactFull.WithoutPrintedScreen,
            withoutNumpad,
            StripFunctionRowTail(withoutNumpad),
            BuildSixtyFive(),
            BuildSixty(),
        ];
    }

    private static KeyLayout BuildFullSize()
    {
        var builder = new LayoutBuilder();

        // 功能键行
        builder.Key(KeyId.Escape, 0, 0);
        builder.Row(2, 0, F(1, 4));
        builder.Row(6.5, 0, F(5, 4));
        builder.Row(11, 0, F(9, 4));
        builder.Row(15.5, 0, K(KeyId.PrintScreen), K(KeyId.ScrollLock), K(KeyId.Pause));

        AddNumberRow(builder, 1, KeyId.Grave);
        builder.Row(16, 1, K(KeyId.Insert), K(KeyId.Home), K(KeyId.PageUp));
        builder.Row(20, 1, K(KeyId.NumLock), K(KeyId.NumpadDivide), K(KeyId.NumpadMultiply), K(KeyId.NumpadSubtract));

        AddTabRow(builder, 2);
        builder.Row(16, 2, K(KeyId.Delete), K(KeyId.End), K(KeyId.PageDown));
        builder.Row(20, 2, K(KeyId.Numpad7), K(KeyId.Numpad8), K(KeyId.Numpad9));
        builder.Key(KeyId.NumpadAdd, 23, 2, height: 2);

        AddCapsRow(builder, 3);
        builder.Row(20, 3, K(KeyId.Numpad4), K(KeyId.Numpad5), K(KeyId.Numpad6));

        AddShiftRow(builder, 4);
        builder.Key(KeyId.ArrowUp, 17, 4);
        builder.Row(20, 4, K(KeyId.Numpad1), K(KeyId.Numpad2), K(KeyId.Numpad3));
        builder.Key(KeyId.NumpadEnter, 23, 4, height: 2);

        AddBottomRow(builder, 5);
        builder.Row(16, 5, K(KeyId.ArrowLeft), K(KeyId.ArrowDown), K(KeyId.ArrowRight));
        builder.Key(KeyId.Numpad0, 20, 5, width: 2);
        builder.Key(KeyId.NumpadDecimal, 22, 5);

        return builder.Build("ansi-104", "全尺寸 104", 24);
    }

    private readonly record struct CompactFullSize(KeyLayout WithPrintedScreen, KeyLayout WithoutPrintedScreen);

    /// <summary>
    /// 紧凑全尺寸：104 的几何，按用户实际键盘去掉 PrtSc / ScrLk / Pause / Ins。
    /// 两套按「有没有 PrtSc / Ins」命名，不写键数：各家的 98 / 99 键排布并不统一，
    /// 与其编一个不准的数字，不如让名称对应用户能一眼认出的那几颗键；
    /// 要逐键还原某一款键盘，用 Data\layouts 里的 JSON 覆盖。
    /// </summary>
    private static CompactFullSize BuildCompactFullSize(KeyLayout full)
    {
        var withoutPause = full.Caps
            .Where(cap => cap.Id is not (KeyId.ScrollLock or KeyId.Pause))
            .ToArray();
        var withoutInsert = withoutPause.Where(cap => cap.Id != KeyId.Insert).ToArray();
        var withoutPrintScreen = withoutInsert.Where(cap => cap.Id != KeyId.PrintScreen).ToArray();

        return new CompactFullSize(
            new KeyLayout("compact-full-prtsc", "紧凑全尺寸 · 带 PrtSc", full.Columns, withoutPause),
            new KeyLayout("compact-full-98", "紧凑全尺寸 · 无 PrtSc / Ins", full.Columns,
                withoutPrintScreen));
    }

    private static KeyLayout StripNumpad(KeyLayout full)
    {
        // 数字键盘占 20U 之后的两列，去掉它剩下的就是 87 键的 TKL。
        var caps = full.Caps.Where(cap => cap.X < 20 - KeyLayout.Tolerance).ToArray();
        return new KeyLayout("tkl-87", "无数字键盘 87", 19, caps);
    }

    private static KeyLayout StripFunctionRowTail(KeyLayout tkl)
    {
        var caps = tkl.Caps
            .Where(cap => cap.Id is not (KeyId.PrintScreen or KeyId.ScrollLock or KeyId.Pause))
            .ToArray();
        return new KeyLayout("compact-84", "75% 84", tkl.Columns, caps);
    }

    /// <summary>65%：60% 的主键区 + 右上三颗导航键 + 右下方向键，共 68 键。</summary>
    private static KeyLayout BuildSixtyFive()
    {
        var builder = new LayoutBuilder();
        AddSixtyPercentBlock(builder);

        builder.Row(16, 0, K(KeyId.Delete), K(KeyId.Home), K(KeyId.PageUp));
        builder.Key(KeyId.ArrowUp, 17, 3);
        builder.Row(16, 4, K(KeyId.ArrowLeft), K(KeyId.ArrowDown), K(KeyId.ArrowRight));

        return builder.Build("compact-68", "65% 68", 19);
    }

    /// <summary>60%：只有主键区五行，Esc 落在数字行最左侧（原来的 ` 位置）。</summary>
    private static KeyLayout BuildSixty()
    {
        var builder = new LayoutBuilder();
        AddSixtyPercentBlock(builder);
        return builder.Build("compact-60", "60%", 15);
    }

    private static void AddSixtyPercentBlock(LayoutBuilder builder)
    {
        AddNumberRow(builder, 0, KeyId.Escape);
        AddTabRow(builder, 1);
        AddCapsRow(builder, 2);
        AddShiftRow(builder, 3);
        AddBottomRow(builder, 4);
    }

    // ---- 主键区五行：104 / 87 / 84 / 60 共用同一套宽度 ----

    private static void AddNumberRow(LayoutBuilder builder, double y, KeyId firstKey) =>
        builder.Row(0, y,
            K(firstKey),
            K(KeyId.Digit1), K(KeyId.Digit2), K(KeyId.Digit3), K(KeyId.Digit4), K(KeyId.Digit5),
            K(KeyId.Digit6), K(KeyId.Digit7), K(KeyId.Digit8), K(KeyId.Digit9), K(KeyId.Digit0),
            K(KeyId.Minus), K(KeyId.Equal), K(KeyId.Backspace, 2));

    private static void AddTabRow(LayoutBuilder builder, double y) =>
        builder.Row(0, y,
            K(KeyId.Tab, 1.5),
            K(KeyId.Q), K(KeyId.W), K(KeyId.E), K(KeyId.R), K(KeyId.T), K(KeyId.Y), K(KeyId.U),
            K(KeyId.I), K(KeyId.O), K(KeyId.P), K(KeyId.LeftBracket), K(KeyId.RightBracket),
            K(KeyId.Backslash, 1.5));

    private static void AddCapsRow(LayoutBuilder builder, double y) =>
        builder.Row(0, y,
            K(KeyId.CapsLock, 1.75),
            K(KeyId.A), K(KeyId.S), K(KeyId.D), K(KeyId.F), K(KeyId.G), K(KeyId.H), K(KeyId.J),
            K(KeyId.K), K(KeyId.L), K(KeyId.Semicolon), K(KeyId.Apostrophe),
            K(KeyId.Enter, 2.25));

    private static void AddShiftRow(LayoutBuilder builder, double y) =>
        builder.Row(0, y,
            K(KeyId.LeftShift, 2.25),
            K(KeyId.Z), K(KeyId.X), K(KeyId.C), K(KeyId.V), K(KeyId.B), K(KeyId.N), K(KeyId.M),
            K(KeyId.Comma), K(KeyId.Period), K(KeyId.Slash),
            K(KeyId.RightShift, 2.75));

    private static void AddBottomRow(LayoutBuilder builder, double y) =>
        builder.Row(0, y,
            K(KeyId.LeftControl, 1.25), K(KeyId.LeftWindows, 1.25), K(KeyId.LeftAlt, 1.25),
            K(KeyId.Space, 6.25),
            K(KeyId.RightAlt, 1.25), K(KeyId.RightWindows, 1.25), K(KeyId.Application, 1.25),
            K(KeyId.RightControl, 1.25));

    private static (KeyId, double)[] F(int first, int count) =>
        Enumerable.Range(first, count)
            .Select(number => ((KeyId)Enum.Parse<KeyId>($"F{number}"), 1d))
            .ToArray();

    private static (KeyId, double) K(KeyId id, double width = 1) => (id, width);
}
