using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keyana.Core;

/// <summary>自定义配列的 JSON 格式。
///
/// <code>
/// {
///   "version": 1,
///   "id": "my-60",
///   "name": "我的 60%",
///   "columns": 15,
///   "keys": [ { "key": "Escape", "x": 0, "y": 0, "w": 1, "h": 1, "label": "Esc" } ]
/// }
/// </code>
///
/// 坐标单位是 1U；<c>key</c> 用 <see cref="KeyId"/> 的枚举名（不区分大小写），
/// <c>label</c> 可省略，省略时用内置短标签。文件放在 <c>Data\layouts</c> 下即可被发现。
/// </summary>
public sealed class KeyLayoutDocument
{
    public int Version { get; set; } = 1;

    public string? Id { get; set; }

    public string? Name { get; set; }

    public double Columns { get; set; }

    public List<KeyCapDocument> Keys { get; set; } = [];
}

public sealed class KeyCapDocument
{
    public string? Key { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double Width { get; set; } = 1;

    [JsonPropertyName("h")]
    public double Height { get; set; } = 1;

    public string? Label { get; set; }
}

/// <summary>自定义配列的读写与校验。解析失败只返回错误说明，不抛异常给界面。</summary>
public static class KeyLayoutJson
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool TryParse(string json, out KeyLayout? layout, out IReadOnlyList<string> errors)
    {
        layout = null;
        var problems = new List<string>();

        KeyLayoutDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<KeyLayoutDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            errors = [$"JSON 解析失败：{exception.Message}"];
            return false;
        }

        if (document is null)
        {
            errors = ["文件内容为空。"];
            return false;
        }

        if (document.Version != CurrentVersion)
        {
            problems.Add($"不支持的配列版本 {document.Version}，当前只认 {CurrentVersion}。");
        }

        var caps = new List<KeyCap>(document.Keys.Count);
        foreach (var (index, cap) in document.Keys.Select((cap, index) => (index, cap)))
        {
            if (string.IsNullOrWhiteSpace(cap.Key))
            {
                problems.Add($"第 {index + 1} 个键缺少 key。");
                continue;
            }

            if (!Enum.TryParse<KeyId>(cap.Key.Trim(), ignoreCase: true, out var id) ||
                !Enum.IsDefined(id))
            {
                problems.Add($"第 {index + 1} 个键的 key「{cap.Key}」不是已知按键。");
                continue;
            }

            caps.Add(new KeyCap(id, cap.X, cap.Y, cap.Width, cap.Height, cap.Label));
        }

        var candidate = new KeyLayout(
            document.Id?.Trim() ?? string.Empty,
            document.Name?.Trim() ?? string.Empty,
            document.Columns,
            caps);

        problems.AddRange(candidate.Validate());
        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        layout = candidate;
        errors = [];
        return true;
    }

    /// <summary>把配列写回 JSON，作为「照着改」的模板。</summary>
    public static string Serialize(KeyLayout layout) => JsonSerializer.Serialize(
        new KeyLayoutDocument
        {
            Version = CurrentVersion,
            Id = layout.Id,
            Name = layout.Name,
            Columns = layout.Columns,
            Keys = layout.Caps
                .Select(cap => new KeyCapDocument
                {
                    Key = cap.Id.ToString(),
                    X = cap.X,
                    Y = cap.Y,
                    Width = cap.Width,
                    Height = cap.Height,
                    Label = cap.Label,
                })
                .ToList(),
        },
        Options);
}
