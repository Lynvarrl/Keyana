using System.Text.Json;

namespace Keyana.Core;

/// <summary>一个键帽在配列画布上的位置，单位是「1U 键宽」，与屏幕像素无关。</summary>
public sealed record KeyCap(
    KeyId Id,
    double X,
    double Y,
    double Width = 1,
    double Height = 1,
    string? Label = null)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? KeyShortLabel.For(Id) : Label;
}

/// <summary>
/// 一套键盘配列：画布宽度（列数，1 列 = 1U）与所有键帽的坐标。
///
/// 配列只影响「怎么画」：哪些键、画在哪、画多宽。采集、入库、排行榜、类别占比
/// 全部按 KeyId 走，与配列无关——换配列不会改变任何一个数字，只是把没画出来的键
/// 从热力图上省掉（它们仍在「全部键位」里能看到次数）。
/// </summary>
public sealed class KeyLayout
{
    public const double Tolerance = 0.001;

    public KeyLayout(string id, string name, double columns, IReadOnlyList<KeyCap> caps)
    {
        Id = id;
        Name = name;
        Columns = columns;
        Caps = caps;
    }

    /// <summary>稳定标识，写进设置文件；重命名显示名不会丢配置。</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>画布宽度，单位 1U。</summary>
    public double Columns { get; }

    public IReadOnlyList<KeyCap> Caps { get; }

    /// <summary>画布高度，单位 1U（取所有键帽下沿的最大值）。</summary>
    public double Rows => Caps.Count == 0 ? 0 : Caps.Max(cap => cap.Y + cap.Height);

    /// <summary>配列里出现的键，用于算「有多少键没画出来」。</summary>
    public IReadOnlySet<KeyId> KeyIds => Caps.Select(cap => cap.Id).ToHashSet();

    /// <summary>
    /// 校验配列是否能安全渲染。导入外部 JSON 时先跑一遍：重叠的键帽会互相盖住、
    /// 重复的 KeyId 会让两次计数画在同一个位置上，都属于肉眼很难发现的错。
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("配列缺少 id。");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("配列缺少 name。");
        }

        if (!double.IsFinite(Columns) || Columns <= 0 || Columns > 64)
        {
            errors.Add($"columns 必须在 0～64 之间，当前是 {Columns}。");
        }

        if (Caps.Count == 0)
        {
            errors.Add("配列里没有任何键。");
            return errors;
        }

        var seen = new HashSet<KeyId>();
        foreach (var cap in Caps)
        {
            if (!seen.Add(cap.Id))
            {
                errors.Add($"键 {cap.Id} 出现了多次。");
            }

            if (!double.IsFinite(cap.X) || !double.IsFinite(cap.Y) ||
                !double.IsFinite(cap.Width) || !double.IsFinite(cap.Height))
            {
                errors.Add($"键 {cap.Id} 的坐标或尺寸不是有效数字。");
                continue;
            }

            if (cap.Width <= 0 || cap.Height <= 0)
            {
                errors.Add($"键 {cap.Id} 的宽高必须大于 0。");
            }

            if (cap.X < -Tolerance || cap.Y < -Tolerance)
            {
                errors.Add($"键 {cap.Id} 的坐标为负：({cap.X}, {cap.Y})。");
            }

            if (cap.X + cap.Width > Columns + Tolerance)
            {
                errors.Add($"键 {cap.Id} 超出画布右边界（{cap.X + cap.Width:F2} > {Columns:F2}）。");
            }
        }

        // 重叠检查：O(n²)，但 n 最多一百多个，导入时跑一次完全够。
        for (var i = 0; i < Caps.Count; i++)
        {
            for (var j = i + 1; j < Caps.Count; j++)
            {
                if (Overlaps(Caps[i], Caps[j]))
                {
                    errors.Add($"键 {Caps[i].Id} 与 {Caps[j].Id} 的键帽重叠。");
                }
            }
        }

        return errors;
    }

    private static bool Overlaps(KeyCap left, KeyCap right) =>
        Math.Abs(left.X - right.X) < Math.Min(left.Width, right.Width) - Tolerance &&
        Math.Abs(left.Y - right.Y) < Math.Min(left.Height, right.Height) - Tolerance;
}

/// <summary>
/// 键帽上的短标签。物理键的完整名称（如「数字键盘 Enter」「浏览器后退」）放不进 1U 键帽，
/// 这里给出与主流键帽一致的缩写；扩展键实在没有通用缩写，才退回完整名称。
/// </summary>
public static class KeyShortLabel
{
    public static string For(KeyId id) => id switch
    {
        KeyId.Escape => "Esc",
        KeyId.Grave => "`",
        KeyId.Minus => "-",
        KeyId.Equal => "=",
        KeyId.Backspace => "Backspace",
        KeyId.Tab => "Tab",
        KeyId.LeftBracket => "[",
        KeyId.RightBracket => "]",
        KeyId.Backslash => "\\",
        KeyId.CapsLock => "Caps",
        KeyId.Semicolon => ";",
        KeyId.Apostrophe => "'",
        KeyId.Enter => "Enter",
        KeyId.LeftShift or KeyId.RightShift => "Shift",
        KeyId.Comma => ",",
        KeyId.Period => ".",
        KeyId.Slash => "/",
        KeyId.LeftControl or KeyId.RightControl => "Ctrl",
        KeyId.LeftWindows or KeyId.RightWindows => "Win",
        KeyId.LeftAlt or KeyId.RightAlt => "Alt",
        KeyId.Space => "Space",
        KeyId.Application => "Menu",
        KeyId.PrintScreen => "PrtSc",
        KeyId.ScrollLock => "ScrLk",
        KeyId.Pause => "Pause",
        KeyId.Insert => "Ins",
        KeyId.Delete => "Del",
        KeyId.Home => "Home",
        KeyId.End => "End",
        KeyId.PageUp => "PgUp",
        KeyId.PageDown => "PgDn",
        KeyId.ArrowUp => "↑",
        KeyId.ArrowDown => "↓",
        KeyId.ArrowLeft => "←",
        KeyId.ArrowRight => "→",
        KeyId.NumLock => "Num",
        KeyId.NumpadDivide => "/",
        KeyId.NumpadMultiply => "*",
        KeyId.NumpadSubtract => "-",
        KeyId.NumpadAdd => "+",
        KeyId.NumpadDecimal => ".",
        KeyId.NumpadEnter => "Enter",
        KeyId.Oem102 => "OEM",
        KeyId.MouseLeftButton => "左键",
        KeyId.MouseRightButton => "右键",
        KeyId.MouseMiddleButton => "中键",
        KeyId.MouseXButton1 => "侧键1",
        KeyId.MouseXButton2 => "侧键2",
        KeyId.MouseWheelUp => "滚轮↑",
        KeyId.MouseWheelDown => "滚轮↓",
        // 手势键画在鼠标一行里，标签要能跟着键帽宽度走：两个字比「滚轮↑」更合适。
        KeyId.WheelGestureUp => "上滑",
        KeyId.WheelGestureDown => "下滑",
        // A-Z、0-9、F1-F24、数字键盘数字：名称本身就是最短标签。
        _ => KeyCatalog.Get(id).DisplayName.Replace("数字键盘 ", string.Empty, StringComparison.Ordinal),
    };
}
