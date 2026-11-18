using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Keyana.Storage;

/// <summary>
/// 把 1.3.1 之前的 <c>key-stats.db</c> 迁移为 <c>keyana.db</c>。
///
/// 不能直接 <see cref="File.Move(string, string)"/>：WAL 模式下已提交但尚未
/// checkpoint 的事务还留在 <c>-wal</c> 里，只搬主库等于静默丢掉最近一段历史。
/// 这里的顺序是「先在旧库上 checkpoint(TRUNCATE) → 切回 journal_mode=DELETE →
/// 确认没有非空 WAL 残留 → 再移动主库」。任何一步不满足都放弃迁移、继续使用旧路径，
/// 绝不留下「主库已搬走、WAL 留在原地」的半成品。
/// </summary>
public static class LegacyDatabaseMigrator
{
    /// <summary>
    /// 尝试迁移。返回 true 表示已迁移到 <paramref name="targetPath"/>；
    /// 返回 false 且 <paramref name="error"/> 为 null 表示无需迁移；
    /// 返回 false 且 <paramref name="error"/> 非 null 表示迁移失败，调用方应继续用旧库。
    /// </summary>
    public static bool TryMigrate(string legacyPath, string targetPath, out string? error)
    {
        error = null;
        var legacyFullPath = Path.GetFullPath(legacyPath);
        var targetFullPath = Path.GetFullPath(targetPath);

        if (string.Equals(legacyFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(legacyFullPath) || File.Exists(targetFullPath))
        {
            return false;
        }

        // 池化连接会持有文件句柄，先全部放掉，否则 checkpoint / 改名都会失败。
        SqliteConnection.ClearAllPools();

        try
        {
            if (!TryCheckpoint(legacyFullPath, out error))
            {
                return false;
            }

            foreach (var sidecar in SidecarPaths(legacyFullPath))
            {
                if (File.Exists(sidecar) && new FileInfo(sidecar).Length > 0)
                {
                    error = $"旧数据库的 {Path.GetFileName(sidecar)} 仍包含未合并的数据，已放弃迁移。";
                    return false;
                }
            }

            File.Move(legacyFullPath, targetFullPath);

            // 走到这里数据已经在主库里；零长度的旁挂文件只是残留，删不掉也不影响读取。
            foreach (var sidecar in SidecarPaths(legacyFullPath))
            {
                try
                {
                    if (File.Exists(sidecar))
                    {
                        File.Delete(sidecar);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryCheckpoint(string path, out string? error)
    {
        error = null;
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var checkpoint = connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = checkpoint.ExecuteReader();
                if (reader.Read() && reader.GetInt64(0) != 0)
                {
                    error = "旧数据库正被其他进程占用，无法安全合并 WAL。";
                    return false;
                }

                // 非 WAL 库返回 (-1,-1)，同样视为无待合并内容。
                if (reader.FieldCount >= 3 && reader.GetInt64(1) >= 0 &&
                    reader.GetInt64(2) < reader.GetInt64(1))
                {
                    error = "旧数据库的 WAL 未能完整并回主库。";
                    return false;
                }
            }

            using (var journalMode = connection.CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
                var mode = Convert.ToString(journalMode.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"无法关闭旧数据库的 WAL 模式（当前 {mode}）。";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string[] SidecarPaths(string databasePath) =>
        [databasePath + "-wal", databasePath + "-shm"];
}
