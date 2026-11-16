using System.Reflection;

namespace KeyStats.App;

/// <summary>
/// 程序版本号的唯一读取入口。
///
/// 版本号本身只在 KeyStats.App.csproj 的 &lt;Version&gt; 里维护一次：
/// FileVersion 与 InformationalVersion 由 SDK 派生，发布脚本读的是同一个 csproj，
/// 窗口标题取这里的值。此前窗口标题里硬编码了一份 v1.1.1，升版本时极容易漏改，
/// 出现「exe 已经是新版本、标题还停在旧版本」的尴尬。
/// </summary>
internal static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 启用源码链接后 SDK 会追加 "+<commit sha>"，展示时不需要。
            var metadataStart = informational.IndexOf('+');
            return metadataStart >= 0 ? informational[..metadataStart] : informational;
        }

        return assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
    }
}
