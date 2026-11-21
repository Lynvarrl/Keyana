using System.Globalization;

namespace Keyana.Core;

/// <summary>
/// 一条「未识别输入」的诊断记录：扫描码、虚拟键、标志位与来源设备。
///
/// 这类事件没有可辨认的扫描码，不进入统计，也不进数据库；记下来只是为了事后分析
/// 「是哪个设备在什么时候造了这条事件」，所以这里只管格式，不碰文件、不碰 Win32。
/// </summary>
public readonly record struct UnrecognizedInputRecord(
    DateTimeOffset At,
    ushort ScanCode,
    ushort VirtualKey,
    ushort Flags,
    string DeviceLabel)
{
    /// <summary>日志首行。以 <c>#</c> 开头，人看是表头，程序读可以跳过。</summary>
    public const string HeaderLine = "# 时间\tScan\tVK\tFlags\t来源设备";

    /// <summary>
    /// 制表符分隔的一行，直接用 Excel 打开就能按列拆开。
    /// 时间带本地时区偏移，跨时区对照时不会含糊。
    /// </summary>
    public string ToLogLine()
    {
        var label = string.IsNullOrWhiteSpace(DeviceLabel) ? "未知设备" : Flatten(DeviceLabel);
        return string.Concat(
            At.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            "\t0x", ScanCode.ToString("X2", CultureInfo.InvariantCulture),
            "\t0x", VirtualKey.ToString("X2", CultureInfo.InvariantCulture),
            "\t0x", Flags.ToString("X2", CultureInfo.InvariantCulture),
            "\t", label);
    }

    /// <summary>设备标签理论上不会有制表符 / 换行，真有也不能把一行日志拆成两行。</summary>
    private static string Flatten(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
