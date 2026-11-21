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

    /// <summary>
    /// 时段条每块几分钟。只影响展示粒度：数据层固定是分钟精度，聚合在界面里做。
    /// 取值必须是 1/2/5/10/15/30/60 之一（都能整除 60，块边界才对得上整点）。
    /// </summary>
    public int TimelineBucketMinutes { get; set; } = 10;

    /// <summary>
    /// 热力图使用的键盘配列 id。找不到对应配列时回落到内置 104；
    /// 只影响画法，不影响任何统计口径。
    /// </summary>
    public string KeyboardLayoutId { get; set; } = "ansi-104";

    /// <summary>
    /// 滚轮手势窗口（毫秒）：距同向上一次滚动超过这个静默时长，才算一次新的上滑 / 下滑动作。
    /// 运行时热更新，只影响改动之后的新事件——判定发生在采集那一刻，历史不会重算。
    /// </summary>
    public int WheelGestureWindowMs { get; set; } = WheelGestureDetector.DefaultWindowMs;

    /// <summary>
    /// 距离换算系数，只用于把滚动距离估算成「厘米 / 米」这类直观长度。
    /// 默认按「一格 3 行、每行 20 DIP」折算；系统缩放不参与（DIP 与 DPI 无关），
    /// 这个系数只用来修正「每次滚几行」的设置差异。任何情况下都不写回存储。
    /// </summary>
    public double WheelDpiScale { get; set; } = 1.0;

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

        // 时段粒度只认阶梯上的值：手改成 7 这种会让块边界不对齐整点。
        if (TimelineBucketMinutes is not (1 or 2 or 5 or 10 or 15 or 30 or 60))
        {
            TimelineBucketMinutes = 10;
        }

        // 配列 id 只做「非空」检查：具体存不存在由配列库判断，找不到就回落 104。
        KeyboardLayoutId = string.IsNullOrWhiteSpace(KeyboardLayoutId)
            ? "ansi-104"
            : KeyboardLayoutId.Trim();

        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? string.Empty : FontFamily.Trim();

        // 滚轮的界面参数同样允许被手工改坏：越界就是「界面字段坏了」，回默认值而不是
        // 硬钳到边界 —— 钳到 50 或 500 会让人以为自己是这么设的。
        if (WheelGestureWindowMs is < WheelGestureDetector.MinWindowMs or > WheelGestureDetector.MaxWindowMs)
        {
            WheelGestureWindowMs = WheelGestureDetector.DefaultWindowMs;
        }

        if (!double.IsFinite(WheelDpiScale) || WheelDpiScale is < 0.1 or > 10)
        {
            WheelDpiScale = 1.0;
        }
    }
}

