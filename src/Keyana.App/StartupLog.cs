using System.Globalization;
using System.IO;
using System.Text;
using Keyana.Core;

namespace Keyana.App;

/// <summary>
/// 启动日志：每次主实例启动时把完整构建信息追加到程序旁的 <c>Data\startup.log</c>。
///
/// 这个程序平时藏在托盘里，报 Bug 时最先要确认的是「跑的是哪一次构建、哪份代码」。
/// 错误日志只记录异常，没法回答这个问题，所以在启动时固定留一条环境记录：
/// 版本、Build ID、Git 提交、工作区状态与构建渠道一次写全。
///
/// 与 ErrorLog 一样，日志自身失败绝不影响启动；文件超过 256 KB 后轮转为
/// <c>startup.log.1</c>，只保留一代，避免长期运行把磁盘写满。
/// </summary>
internal static class StartupLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "startup.log");

    /// <summary>记录一次启动。本方法不会抛出。</summary>
    public static void RecordStartup(BuildInfo build)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new StringBuilder()
                .Append('[')
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append("] Keyana 启动 · ")
                .Append(build.DisplayVersion)
                .Append(" · build ")
                .AppendLine(build.BuildId)
                .AppendLine(build.ToDiagnosticText())
                .AppendLine(new string('-', 72))
                .ToString();

            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不能反过来干扰启动流程。
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        File.Move(LogPath, LogPath + ".1", overwrite: true);
    }
}
