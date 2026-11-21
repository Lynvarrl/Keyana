using System.Text;
using Keyana.Core;

namespace Keyana.Storage;

/// <summary>
/// 「未识别输入」的追加式日志文件：一行一条，制表符分隔，开头一行表头。
///
/// 刻意做成可注入路径的普通类，而不是又一个静态单例：轮转、表头只写一次、
/// 关掉开关后不再碰文件，这些都值得用临时目录直接测。
///
/// <see cref="Enabled"/> 默认 false —— 这条日志带设备硬件标识，是否落盘由上层（设置页）决定。
/// </summary>
public sealed class UnrecognizedInputLog
{
    private readonly object _gate = new();
    private readonly long _maxBytes;

    public UnrecognizedInputLog(string path, long maxBytes = 512 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        FilePath = path;
        _maxBytes = maxBytes;
    }

    public string FilePath { get; }

    /// <summary>关着的时候连文件都不碰。</summary>
    public bool Enabled { get; set; }

    /// <summary>追加一条记录。本方法不会抛出：诊断日志不能反过来影响采集。</summary>
    public void Append(UnrecognizedInputRecord record)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var directory = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var exists = File.Exists(FilePath);
                if (exists && new FileInfo(FilePath).Length >= _maxBytes)
                {
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                    exists = false;
                }

                var builder = new StringBuilder();
                if (!exists)
                {
                    builder.AppendLine(UnrecognizedInputRecord.HeaderLine);
                }

                builder.AppendLine(record.ToLogLine());
                File.AppendAllText(FilePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 目录只读、磁盘满、文件被别的程序锁住：都不该影响采集。
        }
    }
}
