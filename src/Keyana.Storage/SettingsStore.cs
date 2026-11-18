using System.Text.Json;

namespace Keyana.Storage;

public sealed class SettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string SettingsPath { get; } = Path.GetFullPath(settingsPath);

    /// <summary>
    /// 最近一次加载是否降级过。非 null 表示设置文件损坏/被占用，已改用默认值——
    /// 调用方应把它写进日志或提示用户，而不是当成正常启动。
    /// </summary>
    public string? LastLoadWarning { get; private set; }

    /// <summary>
    /// 同步读取一份设置快照，供启动阶段（窗口尚未创建）决定主题与字体。
    /// 任何读取或解析失败都返回 null，由调用方回落到默认值——启动路径不能因为
    /// 一个坏掉的设置文件而中断。
    /// </summary>
    public static AppSettings? TryLoad(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            settings?.Validate();
            return settings;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AppSettings> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        LastLoadWarning = null;
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("设置文件为空或格式不正确。");
            settings.Validate();
            return settings;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // 设置只影响主题/字体/刷新间隔等外观与调度参数。旧实现在这里直接抛出，
            // 会让整个存储初始化失败——采集照常进行、却全部不入库，是更坏的结果。
            // 这里退回默认值继续启动，把原因交给调用方记录。
            LastLoadWarning = $"设置文件无法读取，已使用默认值：{exception.Message}";
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("设置文件缺少父目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

