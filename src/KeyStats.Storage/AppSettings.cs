namespace KeyStats.Storage;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public int FlushIntervalSeconds { get; set; } = 30;

    public string HeatPalette { get; set; } = "BlueMagenta";

    public void Validate()
    {
        if (Version != 1)
        {
            throw new InvalidDataException($"不支持的设置版本：{Version}。");
        }

        if (FlushIntervalSeconds is < 5 or > 300)
        {
            throw new InvalidDataException("FlushIntervalSeconds 必须在 5～300 秒之间。");
        }
    }
}

