using Keyana.Core;

namespace Keyana.Storage;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public int FlushIntervalSeconds { get; set; } = 30;

    public string HeatPalette { get; set; } = "BlueMagenta";

    /// <summary>System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>空字符串表示跟随系统默认字体。</summary>
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>界面字号倍率，1.0 为标准。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>自定义范围是否向外吸附到 10 分钟边界。</summary>
    public bool SnapToTenMinutes { get; set; } = true;

    /// <summary>
    /// 键盘键位的着色基准：KeyboardOnly / MouseOnly / KeyboardAndMouse。
    /// 默认只和键盘自己比——鼠标左键的次数通常远高于任何单个键，
    /// 混在一起算会把整个键盘压成浅色。
    /// </summary>
    public string KeyboardColorBasis { get; set; } = "KeyboardOnly";

    /// <summary>鼠标按键与滚轮的着色基准，默认和键盘 + 鼠标的总量级比。</summary>
    public string MouseColorBasis { get; set; } = "KeyboardAndMouse";

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

        // 界面相关的字段允许被手工改坏：这里就地降级为默认值，
        // 不让一个拼错的色板名或字号把程序挡在启动之前。
        if (Theme is not ("System" or "Light" or "Dark"))
        {
            Theme = "System";
        }

        if (!double.IsFinite(FontScale) || FontScale is < 0.8 or > 1.5)
        {
            FontScale = 1.0;
        }

        // 着色基准同样允许被手工改坏：认不出的名字回到该字段的默认值，
        // 不让一个拼错的枚举名把热力图整片刷成空色。
        if (!Enum.TryParse<ColorBasis>(KeyboardColorBasis, ignoreCase: true, out _))
        {
            KeyboardColorBasis = "KeyboardOnly";
        }

        if (!Enum.TryParse<ColorBasis>(MouseColorBasis, ignoreCase: true, out _))
        {
            MouseColorBasis = "KeyboardAndMouse";
        }

        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? string.Empty : FontFamily.Trim();
    }
}

