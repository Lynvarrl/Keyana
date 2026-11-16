using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KeyStats.App.Controls;
using KeyStats.Storage;
using Microsoft.Win32;

namespace KeyStats.App.Theme;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// 运行时换肤：替换合并资源字典、按倍率刷新字号、切换窗口标题栏明暗。
///
/// 主题字典必须在创建窗口之前装好，否则首帧会先画一遍浅色再跳成深色。
/// 启动路径用 <see cref="Initialize"/> 同步读一次 settings.json 就是为了这个。
/// </summary>
public static class ThemeManager
{
    private const string DefaultFontStack =
        "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private static readonly (string Key, double BaseSize)[] FontSizes =
    [
        ("FontSizeCaption", 11),
        ("FontSizeSmall", 12),
        ("FontSizeBody", 13),
        ("FontSizeSubtitle", 15),
        ("FontSizeTitle", 18),
        ("FontSizePageTitle", 26),
        ("FontSizeMetric", 30),
        ("FontSizeMetricLarge", 40),
    ];

    private static ResourceDictionary? _themeDictionary;

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static bool IsDark => Current == AppTheme.Dark;

    public static double FontScale { get; private set; } = 1.0;

    public static string FontFamilyName { get; private set; } = string.Empty;

    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");
        ApplyFromSettings(SettingsStore.TryLoad(settingsPath));
    }

    public static void ApplyFromSettings(AppSettings? settings) =>
        Apply(
            ResolveTheme(settings?.Theme ?? "System"),
            settings?.FontFamily ?? string.Empty,
            settings?.FontScale ?? 1.0);

    public static AppTheme ResolveTheme(string setting) => setting switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => IsSystemDark() ? AppTheme.Dark : AppTheme.Light,
    };

    public static void Apply(AppTheme theme, string fontFamily, double fontScale)
    {
        if (Application.Current is null)
        {
            return;
        }

        fontFamily ??= string.Empty;
        fontScale = double.IsFinite(fontScale) ? Math.Clamp(fontScale, 0.8, 1.5) : 1.0;

        var unchanged = theme == Current &&
                        Math.Abs(fontScale - FontScale) < 0.001 &&
                        string.Equals(fontFamily, FontFamilyName, StringComparison.Ordinal) &&
                        _themeDictionary is not null;
        if (unchanged)
        {
            return;
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(theme == AppTheme.Dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

        var insertIndex = 0;
        if (_themeDictionary is not null)
        {
            var existingIndex = dictionaries.IndexOf(_themeDictionary);
            if (existingIndex >= 0)
            {
                insertIndex = existingIndex;
                dictionaries.RemoveAt(existingIndex);
            }
        }

        dictionaries.Insert(insertIndex, next);
        _themeDictionary = next;

        Current = theme;
        FontScale = fontScale;
        FontFamilyName = fontFamily;
        ApplyFontResources(fontScale, fontFamily);
        ControlTheme.Apply(theme == AppTheme.Dark);
        HeatPaletteColors.ApplyTheme(theme == AppTheme.Dark);
        ThemeChanged?.Invoke();
    }

    /// <summary>把窗口标题栏切成与内容一致的明暗，避免深色界面顶着一条白条。</summary>
    public static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return;
        }

        var useDark = IsDark ? 1 : 0;
        // 20 是 20H1 之后的属性号，19 是 1809/1903 的旧编号。两个都写一遍：
        // 某些旧版本对 20 返回成功但实际不生效，只试一个会留下一条不跟随主题的标题栏。
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref useDark, sizeof(int));

        // 标题栏由 DWM 绘制，改完属性后要触发一次非客户区重算，否则要等窗口下次激活才换色。
        SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static void ApplyFontResources(double scale, string fontFamily)
    {
        var resources = Application.Current.Resources;
        foreach (var (key, baseSize) in FontSizes)
        {
            resources[key] = Math.Round(baseSize * scale, 1);
        }

        resources["AppFontFamily"] = string.IsNullOrWhiteSpace(fontFamily)
            ? new FontFamily(DefaultFontStack)
            : new FontFamily(fontFamily);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is not null && Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
