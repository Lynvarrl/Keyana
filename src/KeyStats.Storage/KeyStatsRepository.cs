using System.Globalization;
using System.Text;
using KeyStats.Core;
using Microsoft.Data.Sqlite;

namespace KeyStats.Storage;

public sealed class KeyStatsRepository : IDisposable
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 版本 n-1 → n 的增量 DDL 清单。新增版本时在这里追加一条
    /// <c>(目标版本, 增量 DDL)</c>，例如 <c>(2, "ALTER TABLE minute_buckets ADD COLUMN ...;")</c>。
    ///
    /// 不要修改已有条目：它们的职责是把老库升到当年的版本，改动会让已经升过级的
    /// 数据库对不上。当前 schema 只到版本 1，因此清单为空。
    /// </summary>
    private static readonly (int TargetVersion, string Sql)[] SchemaMigrations = [];

    private readonly string _connectionString;

    public KeyStatsRepository(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("数据库路径缺少父目录。");
        Directory.CreateDirectory(directory);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA wal_autocheckpoint=256;", cancellationToken);

        var integrity = await ExecuteScalarStringAsync(connection, "PRAGMA quick_check;", cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite 快速完整性检查失败：{integrity}");
        }

        var schemaVersion = checked((int)await ExecuteScalarLongAsync(
            connection,
            "PRAGMA user_version;",
            cancellationToken));
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"数据库版本 {schemaVersion} 高于当前程序支持的 {CurrentSchemaVersion}，已拒绝修改。");
        }

        await ApplySchemaMigrationsAsync(connection, schemaVersion, cancellationToken);
    }

    /// <summary>
    /// 把数据库逐版本推进到 <see cref="CurrentSchemaVersion"/>。
    ///
    /// 每一步单独开事务，并同时推进 <c>PRAGMA user_version</c> 与
    /// <c>app_meta.schema_version</c>，两者必须保持一致——前者是程序判断版本的依据，
    /// 后者是可读的元数据，历史上曾经各写各的。
    ///
    /// 迁移结束后校验版本确实到位：如果只把 <see cref="CurrentSchemaVersion"/> 调大
    /// 却忘了往 <see cref="SchemaMigrations"/> 里加条目，这里会立刻抛异常，
    /// 而不是让程序对着半新半旧的库继续读写。
    /// </summary>
    private static async Task ApplySchemaMigrationsAsync(
        SqliteConnection connection,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        if (currentVersion == 0)
        {
            await CreateSchemaAsync(connection, cancellationToken);
            currentVersion = 1;
        }

        foreach (var (targetVersion, sql) in SchemaMigrations)
        {
            if (currentVersion >= targetVersion)
            {
                continue;
            }

            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, transaction, sql, cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"PRAGMA user_version={targetVersion};",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                INSERT INTO app_meta(name, value) VALUES('schema_version', '{targetVersion}')
                ON CONFLICT(name) DO UPDATE SET value = excluded.value;
                """,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            currentVersion = targetVersion;
        }

        if (currentVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"数据库迁移不完整：已到版本 {currentVersion}，期望 {CurrentSchemaVersion}。" +
                "请检查 KeyStatsRepository.SchemaMigrations 是否缺少对应的迁移条目。");
        }
    }

    public async Task<MinuteBucketSnapshot?> GetMinuteBucketAsync(
        long bucketStartUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT keymap_version, counts
            FROM minute_buckets
            WHERE bucket_start_utc = $start;
            """;
        command.Parameters.AddWithValue("$start", bucketStartUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        ValidateKeyMapVersion(reader.GetInt32(0));
        return new MinuteBucketSnapshot(bucketStartUtc, CountVectorCodec.Decode((byte[])reader[1]));
    }

    public async Task UpsertMinuteBucketAsync(
        MinuteBucketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ValidateMinuteSnapshot(snapshot);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO minute_buckets (
                    bucket_start_utc, keymap_version, counts, total_count, updated_at_utc)
                VALUES ($start, $keymap, $counts, $total, $updated)
                ON CONFLICT(bucket_start_utc) DO UPDATE SET
                    keymap_version = excluded.keymap_version,
                    counts = excluded.counts,
                    total_count = excluded.total_count,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$start", snapshot.BucketStartUtc);
            command.Parameters.AddWithValue("$keymap", CountVectorCodec.KeyMapVersion);
            command.Parameters.AddWithValue("$counts", CountVectorCodec.Encode(snapshot.Counts));
            command.Parameters.AddWithValue("$total", snapshot.TotalCount);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await RebuildTenMinuteRollupAsync(connection, transaction, snapshot.BucketStartUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StatisticsAggregate> QueryAggregateAsync(
        long startUtc,
        long endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var totals = new ulong[KeyCatalog.Count];
        var rollupStart = Math.Min(TimeBuckets.AlignUp(startUtc, TimeBuckets.TenMinuteSeconds), endUtc);
        var rollupEnd = Math.Max(TimeBuckets.AlignDown(endUtc, TimeBuckets.TenMinuteSeconds), rollupStart);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "minute_buckets",
            startUtc,
            rollupStart,
            totals,
            cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "rollup_10m",
            rollupStart,
            rollupEnd,
            totals,
            cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "minute_buckets",
            rollupEnd,
            endUtc,
            totals,
            cancellationToken);

        return new StatisticsAggregate(startUtc, endUtc, totals);
    }

    public async Task<StoredDataRange?> GetDataRangeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(bucket_start_utc), MAX(bucket_start_utc) FROM minute_buckets;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return new StoredDataRange(reader.GetInt64(0), checked(reader.GetInt64(1) + TimeBuckets.MinuteSeconds));
    }

    public async Task<ulong> QueryTotalCountAsync(
        long? excludedBucketStartUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (excludedBucketStartUtc is { } excludedBucket)
        {
            command.CommandText = "SELECT COALESCE(SUM(total_count), 0) FROM minute_buckets WHERE bucket_start_utc <> $excluded;";
            command.Parameters.AddWithValue("$excluded", excludedBucket);
        }
        else
        {
            command.CommandText = "SELECT COALESCE(SUM(total_count), 0) FROM minute_buckets;";
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0UL : checked((ulong)Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<TenMinuteBucketTotal>> QueryTenMinuteTotalsAsync(
        long startUtc,
        long endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var totals = new Dictionary<long, ulong>();
        var rollupStart = Math.Min(TimeBuckets.AlignUp(startUtc, TimeBuckets.TenMinuteSeconds), endUtc);
        var rollupEnd = Math.Max(TimeBuckets.AlignDown(endUtc, TimeBuckets.TenMinuteSeconds), rollupStart);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await AccumulateMinuteTotalsAsync(connection, startUtc, rollupStart, totals, cancellationToken);
        await AccumulateBucketTotalsAsync(
            connection,
            "rollup_10m",
            rollupStart,
            rollupEnd,
            totals,
            cancellationToken);
        await AccumulateMinuteTotalsAsync(connection, rollupEnd, endUtc, totals, cancellationToken);

        return totals
            .OrderBy(pair => pair.Key)
            .Select(pair => new TenMinuteBucketTotal(pair.Key, pair.Value))
            .ToArray();
    }

    /// <summary>
    /// 按本地自然日汇总输入量与使用时长。
    ///
    /// 必须在分钟粒度上分组：使用时长定义为「有输入的分钟数」，十分钟汇总表会把它抹掉。
    /// 逐行读取而不是在 SQL 里按日期分组，是为了让跨夏令时的日期边界仍然按真实本地零点计算。
    /// </summary>
    public async Task<DailyUsageSummary> QueryDailyUsageAsync(
        long startUtc,
        long endUtc,
        TimeZoneInfo? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var zone = timeZone ?? TimeZoneInfo.Local;
        var perDay = new Dictionary<DateOnly, (ulong Total, int Minutes)>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start_utc, total_count
            FROM minute_buckets
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var total = checked((ulong)reader.GetInt64(1));
            var localTime = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                zone);
            var date = DateOnly.FromDateTime(localTime.DateTime);

            perDay.TryGetValue(date, out var existing);
            perDay[date] = (checked(existing.Total + total), existing.Minutes + (total > 0 ? 1 : 0));
        }

        // 没有任何输入的日子不会产生分钟行，但日均和使用时长要按自然日跨度算，
        // 否则「10 天里有 1 天在用」会被算成日均 = 当天输入 / 1。这里把区间内的空日补齐。
        var firstDate = LocalDate(startUtc, zone);
        var lastDate = LocalDate(endUtc - 1, zone);
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            perDay.TryAdd(date, (0, 0));
        }

        return DailyUsageSummary.From(perDay
            .OrderBy(pair => pair.Key)
            .Select(pair => new DailyUsage(pair.Key, pair.Value.Total, pair.Value.Minutes)));
    }

    private static DateOnly LocalDate(long utcSeconds, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utcSeconds), zone).DateTime);

    public async Task ExportCsvAsync(
        long startUtc,
        long endUtc,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("导出路径缺少父目录。");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = fullDestinationPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_start_utc, keymap_version, counts
                FROM minute_buckets
                WHERE bucket_start_utc >= $start AND bucket_start_utc < $end
                ORDER BY bucket_start_utc;
                """;
            command.Parameters.AddWithValue("$start", startUtc);
            command.Parameters.AddWithValue("$end", endUtc);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync("minute_start_local,minute_start_utc,key_id,key_name,count");

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var minuteStart = reader.GetInt64(0);
                    ValidateKeyMapVersion(reader.GetInt32(1));
                    var counts = CountVectorCodec.Decode((byte[])reader[2]);
                    var localTime = DateTimeOffset.FromUnixTimeSeconds(minuteStart).ToLocalTime();

                    for (var keyIndex = 0; keyIndex < counts.Length; keyIndex++)
                    {
                        if (counts[keyIndex] == 0)
                        {
                            continue;
                        }

                        var key = KeyCatalog.Get((KeyId)keyIndex);
                        await writer.WriteLineAsync(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{localTime:yyyy-MM-dd HH:mm},{minuteStart},{key.Id},{CsvEscape(key.DisplayName)},{counts[keyIndex]}"));
                    }
                }

                await writer.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<string> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
    }

    public long GetStorageSizeBytes()
    {
        var total = 0L;
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                total = checked(total + new FileInfo(path).Length);
            }
        }

        return total;
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA busy_timeout=5000;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA wal_autocheckpoint=256;", cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string schema = """
            CREATE TABLE IF NOT EXISTS minute_buckets (
                bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 60 = 0),
                keymap_version INTEGER NOT NULL,
                counts BLOB NOT NULL,
                total_count INTEGER NOT NULL CHECK(total_count >= 0),
                updated_at_utc INTEGER NOT NULL
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS rollup_10m (
                bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 600 = 0),
                keymap_version INTEGER NOT NULL,
                counts BLOB NOT NULL,
                total_count INTEGER NOT NULL CHECK(total_count >= 0)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS app_meta (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;

            INSERT INTO app_meta(name, value) VALUES('schema_version', '1')
            ON CONFLICT(name) DO UPDATE SET value = excluded.value;
            PRAGMA user_version=1;
            """;
        await ExecuteNonQueryAsync(connection, transaction, schema, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RebuildTenMinuteRollupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long changedMinuteStart,
        CancellationToken cancellationToken)
    {
        var rollupStart = TimeBuckets.ToTenMinuteStart(changedMinuteStart);
        var rollupCounts = new ulong[KeyCatalog.Count];
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT keymap_version, counts
                FROM minute_buckets
                WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
                """;
            query.Parameters.AddWithValue("$start", rollupStart);
            query.Parameters.AddWithValue("$end", rollupStart + TimeBuckets.TenMinuteSeconds);

            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ValidateKeyMapVersion(reader.GetInt32(0));
                AddCounts(rollupCounts, CountVectorCodec.Decode((byte[])reader[1]));
            }
        }

        var encodedCounts = new uint[KeyCatalog.Count];
        long total = 0;
        for (var index = 0; index < rollupCounts.Length; index++)
        {
            encodedCounts[index] = checked((uint)rollupCounts[index]);
            total = checked(total + encodedCounts[index]);
        }

        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO rollup_10m (bucket_start_utc, keymap_version, counts, total_count)
            VALUES ($start, $keymap, $counts, $total)
            ON CONFLICT(bucket_start_utc) DO UPDATE SET
                keymap_version = excluded.keymap_version,
                counts = excluded.counts,
                total_count = excluded.total_count;
            """;
        upsert.Parameters.AddWithValue("$start", rollupStart);
        upsert.Parameters.AddWithValue("$keymap", CountVectorCodec.KeyMapVersion);
        upsert.Parameters.AddWithValue("$counts", CountVectorCodec.Encode(encodedCounts));
        upsert.Parameters.AddWithValue("$total", total);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AccumulateRowsAsync(
        SqliteConnection connection,
        string tableName,
        long startUtc,
        long endUtc,
        ulong[] destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        if (tableName is not ("minute_buckets" or "rollup_10m"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT keymap_version, counts
            FROM {tableName}
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ValidateKeyMapVersion(reader.GetInt32(0));
            AddCounts(destination, CountVectorCodec.Decode((byte[])reader[1]));
        }
    }

    private static async Task AccumulateMinuteTotalsAsync(
        SqliteConnection connection,
        long startUtc,
        long endUtc,
        Dictionary<long, ulong> destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start_utc, total_count
            FROM minute_buckets
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bucketStart = TimeBuckets.ToTenMinuteStart(reader.GetInt64(0));
            AddBucketTotal(destination, bucketStart, checked((ulong)reader.GetInt64(1)));
        }
    }

    private static async Task AccumulateBucketTotalsAsync(
        SqliteConnection connection,
        string tableName,
        long startUtc,
        long endUtc,
        Dictionary<long, ulong> destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT bucket_start_utc, total_count
            FROM {tableName}
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AddBucketTotal(destination, reader.GetInt64(0), checked((ulong)reader.GetInt64(1)));
        }
    }

    private static void AddBucketTotal(Dictionary<long, ulong> destination, long bucketStart, ulong count)
    {
        destination.TryGetValue(bucketStart, out var existing);
        destination[bucketStart] = checked(existing + count);
    }

    private static void AddCounts(ulong[] destination, uint[] source)
    {
        if (destination.Length != source.Length)
        {
            throw new InvalidDataException("计数向量长度不一致。");
        }

        for (var index = 0; index < source.Length; index++)
        {
            destination[index] = checked(destination[index] + source[index]);
        }
    }

    private static void ValidateMinuteSnapshot(MinuteBucketSnapshot snapshot)
    {
        if (snapshot.BucketStartUtc % TimeBuckets.MinuteSeconds != 0)
        {
            throw new ArgumentException("分钟分段起点没有对齐到整分钟。", nameof(snapshot));
        }

        if (snapshot.Counts.Length != KeyCatalog.Count)
        {
            throw new ArgumentException("分钟分段计数向量长度不正确。", nameof(snapshot));
        }
    }

    private static void ValidateRange(long startUtc, long endUtc)
    {
        if (startUtc % TimeBuckets.MinuteSeconds != 0 || endUtc % TimeBuckets.MinuteSeconds != 0)
        {
            throw new ArgumentException("查询范围必须对齐到整分钟。", nameof(startUtc));
        }

        if (endUtc <= startUtc)
        {
            throw new ArgumentException("结束时间必须晚于开始时间。", nameof(endUtc));
        }
    }

    private static void ValidateKeyMapVersion(int keyMapVersion)
    {
        if (keyMapVersion < CountVectorCodec.MinimumSupportedKeyMapVersion ||
            keyMapVersion > CountVectorCodec.KeyMapVersion)
        {
            throw new InvalidDataException($"不支持的 Key Map 版本：{keyMapVersion}。");
        }
    }

    private static string CsvEscape(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)
            ?? string.Empty;
    }
}
