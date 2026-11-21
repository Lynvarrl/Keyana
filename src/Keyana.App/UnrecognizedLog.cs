using System.IO;
using Keyana.Core;
using Keyana.Storage;

namespace Keyana.App;

/// <summary>
/// 把「未识别输入」接到程序旁的 <c>Data\unrecognized.log</c>。
///
/// 存在的理由：这类事件几小时才冒一条，靠人盯着界面记时间根本对不上——
/// 写到文件里，隔一天再回头看「哪个设备在什么时候发了什么」，才谈得上分析。
///
/// 三条边界（写文件的细节在 <see cref="UnrecognizedInputLog"/>）：
/// 1. <b>默认关闭</b>：日志带来源设备的硬件标识，而 PRIVACY 承诺过不保存设备身份，
///    所以必须由用户在设置页显式打开；
/// 2. <b>只写文件</b>：不进数据库、不进计数向量、不参与任何统计；
/// 3. 轮转与表头由 Storage 那一层负责，这里只做路径与开关的接线。
/// </summary>
internal static class UnrecognizedLog
{
    private static readonly UnrecognizedInputLog Writer =
        new(Path.Combine(AppContext.BaseDirectory, "Data", "unrecognized.log"));

    public static string LogPath => Writer.FilePath;

    /// <summary>由设置页控制；关着的时候连文件都不碰。</summary>
    public static bool Enabled
    {
        get => Writer.Enabled;
        set => Writer.Enabled = value;
    }

    public static void Record(UnrecognizedInputRecord record) => Writer.Append(record);
}
