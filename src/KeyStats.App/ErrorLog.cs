using System.Globalization;
using System.IO;
using System.Text;

namespace KeyStats.App;

/// <summary>
/// 未处理异常的最小落地日志，写入程序旁的 <c>Data\error.log</c>。
///
/// 这是一个登录自启、常驻托盘的后台程序：异常如果只弹一个对话框，用户点掉之后
/// 就再也找不到线索；如果连对话框都没有，对外表现就是「统计突然不涨了」。
/// 所以这里保证任何未处理异常至少留下一条可追溯的记录。
///
/// 两个防爆措施：
/// 1. 同一签名（来源 + 异常类型 + 首行消息）在 10 秒内的重复异常只记一条，
///    避免 150 ms 刷新定时器里反复抛异常把日志刷爆；
/// 2. 日志超过 1 MB 后轮转为 <c>error.log.1</c>，只保留一代。
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(10);
    private static string? _logPath;
    private static string? _lastSignature;
    private static DateTimeOffset _lastWrittenUtc;

    public static string LogPath => _logPath ??= Path.Combine(AppContext.BaseDirectory, "Data", "error.log");

    /// <summary>记录一次未处理异常。本方法不会抛出。</summary>
    public static void Record(string source, Exception exception)
    {
        try
        {
            var signature = BuildSignature(source, exception);
            lock (Gate)
            {
                var now = DateTimeOffset.UtcNow;
                if (string.Equals(signature, _lastSignature, StringComparison.Ordinal) &&
                    now - _lastWrittenUtc < DuplicateWindow)
                {
                    return;
                }

                _lastSignature = signature;
                _lastWrittenUtc = now;
            }

            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfNeeded();

            var entry = new StringBuilder()
                .Append('[')
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append("] ")
                .AppendLine(source)
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 72))
                .ToString();

            File.AppendAllText(LogPath, entry, Encoding.UTF8);
        }
        catch
        {
            // 日志自身失败绝不能反过来影响主流程。
        }
    }

    private static string BuildSignature(string source, Exception exception)
    {
        var message = exception.Message;
        var lineBreak = message.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
        {
            message = message[..lineBreak];
        }

        return string.Concat(source, '|', exception.GetType().FullName, '|', message);
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
