namespace Keyana.Core;

/// <summary>
/// 把 Raw Input 报上来的设备接口路径压成一句可读标签，用来定位「未识别」事件来自哪台设备。
/// 例如 \\?\HID#VID_046D&amp;PID_C52B&amp;MI_00#... 会变成 VID_046D&amp;PID_C52B。
/// 这里只做字符串处理，不碰 Win32；查路径的活由事件源负责。
/// </summary>
public static class DevicePathLabel
{
    /// <summary>从完整设备路径里提取硬件标识；提不出来时退回设备类和实例名。</summary>
    public static string FromPath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "未知设备";
        }

        var vendor = ReadHexToken(devicePath, "VID_");
        var product = ReadHexToken(devicePath, "PID_");
        if (vendor is not null && product is not null)
        {
            return $"VID_{vendor}&PID_{product}";
        }

        if (vendor is not null)
        {
            return $"VID_{vendor}";
        }

        if (product is not null)
        {
            return $"PID_{product}";
        }

        var body = devicePath.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? devicePath[4..]
            : devicePath;

        var firstHash = body.IndexOf('#');
        if (firstHash < 0)
        {
            return body.Length == 0 ? "未知设备" : body;
        }

        var deviceClass = body[..firstHash];
        var remainder = body[(firstHash + 1)..];
        var secondHash = remainder.IndexOf('#');
        var instance = secondHash < 0 ? remainder : remainder[..secondHash];

        if (instance.Length == 0)
        {
            return deviceClass.Length == 0 ? "未知设备" : deviceClass;
        }

        return $"{deviceClass}\\{instance}";
    }

    private static string? ReadHexToken(string path, string prefix)
    {
        var index = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + prefix.Length;
        var end = start;
        while (end < path.Length && Uri.IsHexDigit(path[end]))
        {
            end++;
        }

        return end == start ? null : path[start..end].ToUpperInvariant();
    }
}
