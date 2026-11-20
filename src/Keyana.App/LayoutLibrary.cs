using System.IO;
using System.Text;
using Keyana.Core;

namespace Keyana.App;

/// <summary>
/// 配列库：内置预设 + <c>Data\layouts\*.json</c> 里用户自己放的文件。
///
/// 放在 Data 目录而不是数据库里，是为了「复制整个程序文件夹就能迁移」这条约定：
/// 配列是一份纯展示配置，跟着文件夹走即可，不需要数据库迁移脚本，也不会因为降级
/// 到旧版本而读不懂新表。文件解析失败只会让那一套配列消失，不影响启动。
/// </summary>
internal sealed class LayoutLibrary
{
    private readonly List<KeyLayout> _layouts = [];
    private readonly List<string> _loadErrors = [];

    public LayoutLibrary(string dataDirectory)
    {
        DirectoryPath = Path.Combine(dataDirectory, "layouts");
        Reload();
    }

    /// <summary>自定义配列目录，界面上可以直接告诉用户把文件放哪。</summary>
    public string DirectoryPath { get; }

    public IReadOnlyList<KeyLayout> Layouts => _layouts;

    /// <summary>上次扫描时被跳过的文件与原因，用于排查「我放的配列怎么没出现」。</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    public KeyLayout? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _layouts.FirstOrDefault(layout => string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 重新扫描：内置预设在前，自定义在后。自定义配列如果和内置同 id，内置优先，
    /// 免得用户误放一个文件就把标准配列顶掉。
    /// </summary>
    public void Reload()
    {
        _layouts.Clear();
        _loadErrors.Clear();
        _layouts.AddRange(KeyLayouts.BuiltIn);

        try
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception exception)
        {
            _loadErrors.Add($"无法创建配列目录 {DirectoryPath}：{exception.Message}");
            return;
        }

        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
        {
            _loadErrors.Add($"无法读取配列目录：{exception.Message}");
            return;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            string json;
            try
            {
                json = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                _loadErrors.Add($"{fileName}：读取失败（{exception.Message}）");
                continue;
            }

            if (!KeyLayoutJson.TryParse(json, out var layout, out var errors) || layout is null)
            {
                _loadErrors.Add($"{fileName}：{string.Join("；", errors)}");
                continue;
            }

            if (KeyLayouts.Find(layout.Id) is not null)
            {
                _loadErrors.Add($"{fileName}：id「{layout.Id}」与内置配列重名，已跳过。");
                continue;
            }

            if (_layouts.Any(existing => string.Equals(existing.Id, layout.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _loadErrors.Add($"{fileName}：id「{layout.Id}」与其他自定义配列重名，已跳过。");
                continue;
            }

            _layouts.Add(layout);
        }
    }

    /// <summary>
    /// 导入一份配列文件：校验通过后复制到 <see cref="DirectoryPath"/> 并立刻可用。
    /// 只复制不移动：用户原始的模板文件留在原处，方便继续改。
    /// </summary>
    public bool TryImport(string sourcePath, out KeyLayout? layout, out string error)
    {
        layout = null;

        string json;
        try
        {
            json = File.ReadAllText(sourcePath, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            error = $"无法读取文件：{exception.Message}";
            return false;
        }

        if (!KeyLayoutJson.TryParse(json, out var parsed, out var errors) || parsed is null)
        {
            error = string.Join("\n", errors);
            return false;
        }

        if (KeyLayouts.Find(parsed.Id) is not null)
        {
            error = $"配列 id「{parsed.Id}」与内置配列重名，请改一个 id 再导入。";
            return false;
        }

        var target = Path.Combine(DirectoryPath, SanitizeFileName(parsed.Id) + ".json");
        try
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            File.Copy(sourcePath, target, overwrite: true);
        }
        catch (Exception exception)
        {
            error = $"复制到配列目录失败：{exception.Message}";
            return false;
        }

        Reload();
        layout = Find(parsed.Id);
        if (layout is null)
        {
            error = "文件已复制，但重新扫描后没有找到这套配列。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>id 会被当成文件名：去掉路径分隔符与非法字符，避免写出目录之外。</summary>
    private static string SanitizeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(id.Length);
        foreach (var character in id)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 || character == '.' ? '_' : character);
        }

        var name = builder.ToString().Trim();
        return name.Length == 0 ? "layout" : name;
    }
}
