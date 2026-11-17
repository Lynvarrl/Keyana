using System.Globalization;
using Keyana.Core;
using Keyana.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("标准字母按扫描码映射", Sync(StandardLetterMapsByScanCode)),
    ("左右修饰键分离", Sync(LeftAndRightModifiersAreDistinct)),
    ("主 Enter 与数字键盘 Enter 分离", Sync(MainAndNumpadEnterAreDistinct)),
    ("导航键与数字键盘分离", Sync(NavigationAndNumpadAreDistinct)),
    ("F13 到 F24 映射", Sync(ExtendedFunctionKeysMap)),
    ("媒体键回退映射", Sync(MediaKeyFallbackMaps)),
    ("媒体虚拟键优先于冲突扫描码", Sync(MediaVirtualKeyWinsOverScanCode)),
    ("长按自动连发只计一次", Sync(RepeatedMakeCountsOnce)),
    ("松开后再次按下重新计数", Sync(ReleaseThenPressCountsAgain)),
    ("两把键盘分别按下会各计一次", Sync(TwoDevicesCountSeparately)),
    ("组合键中的每个物理键分别计数", Sync(ChordKeysCountSeparately)),
    ("系统切换丢失松开消息后可恢复计数", Sync(SystemSwitchReleaseCanBeReconciled)),
    ("Alt+Tab 补偿与 Raw Input 不会重复计数", Sync(AltTabFallbackAndRawInputAreDeduplicated)),
    ("鼠标左 / 中 / 右 / 侧键分别计数且按住不连发", Sync(MouseButtonsCountSeparately)),
    ("鼠标侧键归入鼠标类别并可读名称", Sync(SideButtonsBelongToMouseCategory)),
    ("暂停期间不计数且恢复不误计长按", Sync(PauseMaintainsPressedState)),
    ("清零不破坏按下状态", Sync(ClearDoesNotBreakPressedState)),
    ("界面清零可重置残留按下状态", Sync(ClearCanResetPressedState)),
    ("设备移除会清理按下状态", Sync(RemovingDeviceClearsPressedState)),
    ("未知键不会写入计数", Sync(UnknownKeyIsIgnored)),
    ("稀疏计数向量往返一致", Sync(CountVectorRoundTrips)),
    ("阶段 2 的 Key Map v1 数据仍可读取", LegacyKeyMapRowsRemainReadableAsync),
    ("数据库一分钟写入与绝对快照更新", RepositoryUpsertIsAbsoluteAsync),
    ("数据库版本号与元数据保持一致", SchemaVersionMetadataStaysConsistentAsync),
    ("重复初始化为空库不会丢失已有数据", ReinitializeKeepsExistingDataAsync),
    ("更高版本的数据库会被拒绝", NewerSchemaVersionIsRejectedAsync),
    ("全部范围边界覆盖首末有效分钟", DataRangeTracksSparseBoundsAsync),
    ("历史总数量可排除当前分钟", TotalCountCanExcludeCurrentMinuteAsync),
    ("十分钟派生汇总正确", TenMinuteRollupIsCorrectAsync),
    ("十分钟时段序列正确处理范围边缘", TenMinuteSeriesHandlesRangeEdgesAsync),
    ("同一分钟重启后继续累计", RecorderResumesCurrentMinuteAsync),
    ("CSV 导出包含分钟与键位", CsvExportContainsMinuteRowsAsync),
    ("设置文件可原子创建和读取", SettingsStoreRoundTripsAsync),
    ("旧设置文件含未知字段仍可读取", LegacySettingsWithUnknownKeysStillLoadsAsync),
    ("预设范围按本地零点与固定长度计算", Sync(PresetRangesUseLocalDayStart)),
    ("昨天范围覆盖完整本地自然日", Sync(YesterdayRangeCoversWholeLocalDay)),
    ("全部范围并入历史数据边界", Sync(AllRangeMergesStoredBounds)),
    ("自定义范围按十分钟向外对齐", Sync(CustomRangeSnapsToTenMinutes)),
    ("滚轮增量按 120 单位累计成刻度", Sync(WheelAccumulatesToNotches)),
    ("滚轮脉冲可连续计数且不进入按下状态", Sync(WheelImpulseCountsRepeatedly)),
    ("逐日汇总识别活跃日、火热日与逐小时分布", DailyUsageMarksActiveAndFireDaysAsync),
    ("界面设置损坏时降级为默认值", UiSettingsFallBackToDefaultsAsync),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
        Console.WriteLine($"FAIL  {name}\n      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"结果：{tests.Length - failures.Count}/{tests.Length} 通过");
if (failures.Count > 0)
{
    Console.WriteLine(string.Join(Environment.NewLine, failures));
    Environment.ExitCode = 1;
}

return;

static Func<Task> Sync(Action action) => () =>
{
    action();
    return Task.CompletedTask;
};

static void StandardLetterMapsByScanCode()
{
    Equal(KeyId.A, Normalize(0x1E, 0x41));
    Equal(KeyId.Q, Normalize(0x10, 0x51));
    Equal(KeyId.Space, Normalize(0x39, 0x20));
}

static void LeftAndRightModifiersAreDistinct()
{
    Equal(KeyId.LeftShift, Normalize(0x2A, 0x10));
    Equal(KeyId.RightShift, Normalize(0x36, 0x10));
    Equal(KeyId.LeftControl, Normalize(0x1D, 0x11));
    Equal(KeyId.RightControl, Normalize(0x1D, 0x11, RawKeyFlags.E0));
    Equal(KeyId.LeftAlt, Normalize(0x38, 0x12));
    Equal(KeyId.RightAlt, Normalize(0x38, 0x12, RawKeyFlags.E0));
}

static void MainAndNumpadEnterAreDistinct()
{
    Equal(KeyId.Enter, Normalize(0x1C, 0x0D));
    Equal(KeyId.NumpadEnter, Normalize(0x1C, 0x0D, RawKeyFlags.E0));
}

static void NavigationAndNumpadAreDistinct()
{
    Equal(KeyId.Numpad7, Normalize(0x47, 0x67));
    Equal(KeyId.Home, Normalize(0x47, 0x24, RawKeyFlags.E0));
    Equal(KeyId.NumpadDecimal, Normalize(0x53, 0x6E));
    Equal(KeyId.Delete, Normalize(0x53, 0x2E, RawKeyFlags.E0));
}

static void ExtendedFunctionKeysMap()
{
    Equal(KeyId.F13, Normalize(0, 0x7C));
    Equal(KeyId.F24, Normalize(0, 0x87));
}

static void MediaKeyFallbackMaps()
{
    Equal(KeyId.MediaPlayPause, Normalize(0, 0xB3, RawKeyFlags.E0));
    Equal(KeyId.VolumeUp, Normalize(0, 0xAF, RawKeyFlags.E0));
}

static void MediaVirtualKeyWinsOverScanCode()
{
    Equal(KeyId.VolumeMute, Normalize(0x22, 0xAD, RawKeyFlags.E0));
}

static void RepeatedMakeCountsOnce()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(1, MakeA());
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ReleaseThenPressCountsAgain()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    var snapshot = counter.GetSnapshot();
    Equal(2L, snapshot.Counts[(int)KeyId.A]);
    Equal(2L, snapshot.TotalCount);
}

static void TwoDevicesCountSeparately()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(2, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ChordKeysCountSeparately()
{
    var counter = new KeyboardCounter();

    // Ctrl+C：两个按下消息都必须分别计数。
    counter.Process(1, new RawKeyEvent(0x1D, RawKeyFlags.None, 0x11));
    counter.Process(1, new RawKeyEvent(0x2E, RawKeyFlags.None, 0x43));
    counter.Process(1, new RawKeyEvent(0x2E, RawKeyFlags.Break, 0x43));

    // Ctrl 保持按下时再按 V：Ctrl 仍是一次物理按下，V 另计一次。
    counter.Process(1, new RawKeyEvent(0x2F, RawKeyFlags.None, 0x56));
    counter.Process(1, new RawKeyEvent(0x2F, RawKeyFlags.Break, 0x56));
    counter.Process(1, new RawKeyEvent(0x1D, RawKeyFlags.Break, 0x11));

    var snapshot = counter.GetSnapshot();
    Equal(1L, snapshot.Counts[(int)KeyId.LeftControl]);
    Equal(1L, snapshot.Counts[(int)KeyId.C]);
    Equal(1L, snapshot.Counts[(int)KeyId.V]);
    Equal(3L, snapshot.TotalCount);
}

static void SystemSwitchReleaseCanBeReconciled()
{
    var counter = new KeyboardCounter();
    var altDown = new RawKeyEvent(0x38, RawKeyFlags.None, 0x12);
    var tabDown = new RawKeyEvent(0x0F, RawKeyFlags.None, 0x09);

    counter.Process(1, altDown);
    counter.Process(1, tabDown);

    // 模拟 Windows 接管 Alt+Tab 后，应用没有收到对应松开消息。
    counter.ReleaseKey(KeyId.LeftAlt);
    counter.ReleaseKey(KeyId.Tab);
    counter.Process(1, altDown);
    counter.Process(1, tabDown);

    var snapshot = counter.GetSnapshot();
    Equal(2L, snapshot.Counts[(int)KeyId.LeftAlt]);
    Equal(2L, snapshot.Counts[(int)KeyId.Tab]);
}

static void AltTabFallbackAndRawInputAreDeduplicated()
{
    var counter = new KeyboardCounter();
    var tabDown = new RawKeyEvent(0x0F, RawKeyFlags.None, 0x09);
    var tabUp = new RawKeyEvent(0x0F, RawKeyFlags.Break, 0x09);

    Equal(true, counter.ProcessButton(0, KeyId.Tab, isBreak: false).Counted);
    Equal(false, counter.ProcessDetailed(7, tabDown).Counted);
    counter.ProcessDetailed(7, tabUp);

    Equal(true, counter.ProcessDetailed(7, tabDown).Counted);
    Equal(false, counter.ProcessButton(0, KeyId.Tab, isBreak: false).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.Tab]);
}

static void MouseButtonsCountSeparately()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false).Counted);
    Equal(false, counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false).Counted);
    counter.ProcessButton(9, KeyId.MouseRightButton, isBreak: false);
    counter.ProcessButton(9, KeyId.MouseMiddleButton, isBreak: false);
    counter.ProcessButton(9, KeyId.MouseXButton1, isBreak: false);
    counter.ProcessButton(9, KeyId.MouseXButton2, isBreak: false);

    var heldSnapshot = counter.GetSnapshot();
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseLeftButton]);
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseRightButton]);
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseMiddleButton]);
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseXButton1]);
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseXButton2]);
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseLeftButton));
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseRightButton));
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseMiddleButton));
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseXButton1));
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseXButton2));

    counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: true);
    counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false);
    counter.ProcessButton(9, KeyId.MouseXButton1, isBreak: true);
    counter.ProcessButton(9, KeyId.MouseXButton1, isBreak: false);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.MouseLeftButton]);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.MouseXButton1]);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.MouseXButton2]);
}

static void SideButtonsBelongToMouseCategory()
{
    foreach (var keyId in new[] { KeyId.MouseXButton1, KeyId.MouseXButton2 })
    {
        Equal("鼠标", KeyCatalog.Get(keyId).Category);
        Equal(true, KeyCatalog.Get(keyId).DisplayName.Contains("侧键", StringComparison.Ordinal));
    }
}

static void PauseMaintainsPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.SetPaused(true);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    counter.SetPaused(false);
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ClearDoesNotBreakPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.ClearCounts();
    counter.Process(1, MakeA());
    Equal(0L, counter.GetSnapshot().Counts[(int)KeyId.A]);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ClearCanResetPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.ClearCounts(resetPressedState: true);
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void RemovingDeviceClearsPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(17, MakeA());
    counter.RemoveDevice(17);
    counter.Process(17, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void UnknownKeyIsIgnored()
{
    var counter = new KeyboardCounter();
    Equal(false, counter.Process(1, new RawKeyEvent(0x7F, RawKeyFlags.None, 0xFE)));
    Equal(0L, counter.GetSnapshot().TotalCount);
}

static void CountVectorRoundTrips()
{
    var counts = new uint[KeyCatalog.Count];
    counts[(int)KeyId.A] = 17;
    counts[(int)KeyId.Space] = 43;
    counts[(int)KeyId.NumpadEnter] = 2;
    counts[(int)KeyId.MouseXButton1] = 9;
    counts[(int)KeyId.MouseXButton2] = 11;
    var encoded = CountVectorCodec.Encode(counts);
    var decoded = CountVectorCodec.Decode(encoded);
    Equal(counts.Length, decoded.Length);
    for (var index = 0; index < counts.Length; index++)
    {
        Equal(counts[index], decoded[index]);
    }
}

static async Task LegacyKeyMapRowsRemainReadableAsync()
{
    await WithRepositoryAsync(async (directory, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        var blob = CountVectorCodec.Encode(NewCounts((KeyId.C, 4), (KeyId.LeftControl, 2)));
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "test.db"),
        }.ToString();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO minute_buckets (
                    bucket_start_utc, keymap_version, counts, total_count, updated_at_utc)
                VALUES ($start, 1, $counts, 6, $start);
                """;
            command.Parameters.AddWithValue("$start", minuteStart);
            command.Parameters.AddWithValue("$counts", blob);
            await command.ExecuteNonQueryAsync();
        }

        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(4UL, aggregate.Counts[(int)KeyId.C]);
        Equal(2UL, aggregate.Counts[(int)KeyId.LeftControl]);
        Equal(0UL, aggregate.Counts[(int)KeyId.MouseLeftButton]);
    });
}

static async Task RepositoryUpsertIsAbsoluteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        var counts = NewCounts((KeyId.A, 3));
        await repository.UpsertMinuteBucketAsync(new MinuteBucketSnapshot(minuteStart, counts));

        var loaded = await repository.GetMinuteBucketAsync(minuteStart);
        Equal(3u, loaded!.Counts[(int)KeyId.A]);

        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(minuteStart, NewCounts((KeyId.A, 5))));
        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(5UL, aggregate.TotalCount);
        Equal("ok", await repository.CheckIntegrityAsync());
    });
}

static async Task DataRangeTracksSparseBoundsAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long firstMinute = 1_800_000_000;
        const long lastMinute = firstMinute + 3_600;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(firstMinute, NewCounts((KeyId.A, 1))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(lastMinute, NewCounts((KeyId.MouseLeftButton, 2))));

        var range = await repository.GetDataRangeAsync();
        Equal(firstMinute, range!.StartUtc);
        Equal(lastMinute + 60, range.EndUtc);
    });
}

static async Task TotalCountCanExcludeCurrentMinuteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long firstMinute = 1_800_000_000;
        const long currentMinute = firstMinute + 60;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(firstMinute, NewCounts((KeyId.A, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(currentMinute, NewCounts((KeyId.W, 3))));

        Equal(5UL, await repository.QueryTotalCountAsync());
        Equal(2UL, await repository.QueryTotalCountAsync(currentMinute));
    });
}

static async Task TenMinuteRollupIsCorrectAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long rollupStart = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(rollupStart, NewCounts((KeyId.A, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(rollupStart + 60, NewCounts((KeyId.B, 3))));

        var aggregate = await repository.QueryAggregateAsync(rollupStart, rollupStart + 600);
        Equal(5UL, aggregate.TotalCount);
        Equal(2UL, aggregate.Counts[(int)KeyId.A]);
        Equal(3UL, aggregate.Counts[(int)KeyId.B]);
    });
}

static async Task TenMinuteSeriesHandlesRangeEdgesAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long baseTime = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 60, NewCounts((KeyId.A, 1))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 600, NewCounts((KeyId.B, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 1_200, NewCounts((KeyId.C, 3))));

        var series = await repository.QueryTenMinuteTotalsAsync(baseTime + 60, baseTime + 1_260);
        Equal(3, series.Count);
        Equal(baseTime, series[0].BucketStartUtc);
        Equal(1UL, series[0].TotalCount);
        Equal(baseTime + 600, series[1].BucketStartUtc);
        Equal(2UL, series[1].TotalCount);
        Equal(baseTime + 1_200, series[2].BucketStartUtc);
        Equal(3UL, series[2].TotalCount);
    });
}

static async Task RecorderResumesCurrentMinuteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
        await using (var recorder = await StatisticsRecorder.CreateAsync(
            repository,
            TimeSpan.FromHours(1),
            clock))
        {
            recorder.RecordKeyPress(KeyId.A);
            await recorder.FlushNowAsync();
        }

        await using (var recorder = await StatisticsRecorder.CreateAsync(
            repository,
            TimeSpan.FromHours(1),
            clock))
        {
            recorder.RecordKeyPress(KeyId.A);
            await recorder.FlushNowAsync();
        }

        var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(2UL, aggregate.TotalCount);
    });
}

static async Task CsvExportContainsMinuteRowsAsync()
{
    await WithRepositoryAsync(async (directory, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(minuteStart, NewCounts((KeyId.Space, 4))));
        var csvPath = Path.Combine(directory, "export.csv");
        await repository.ExportCsvAsync(minuteStart, minuteStart + 60, csvPath);
        var csv = await File.ReadAllTextAsync(csvPath);
        Contains("minute_start_local", csv);
        Contains("Space", csv);
        Contains(",4", csv);
    });
}

static async Task SettingsStoreRoundTripsAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var settings = await store.LoadOrCreateAsync();
        Equal(30, settings.FlushIntervalSeconds);
        settings.FlushIntervalSeconds = 45;
        await store.SaveAsync(settings);
        Equal(45, (await store.LoadOrCreateAsync()).FlushIntervalSeconds);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task LegacySettingsWithUnknownKeysStillLoadsAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        // 旧版 settings.json 里有 countKeyRepeat / retentionDays 两个从未实现过的
        // 字段，模型里删掉之后必须仍能读进来，否则升级会让用户丢掉全部设置。
        var settingsPath = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "version": 1,
              "flushIntervalSeconds": 60,
              "countKeyRepeat": false,
              "retentionDays": null,
              "heatPalette": "GreenAmber"
            }
            """);

        var settings = await new SettingsStore(settingsPath).LoadOrCreateAsync();
        Equal(60, settings.FlushIntervalSeconds);
        Equal("GreenAmber", settings.HeatPalette);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task SchemaVersionMetadataStaysConsistentAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var databasePath = Path.Combine(directory, "schema.db");
        using (var repository = new KeyanaRepository(databasePath))
        {
            await repository.InitializeAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var expected = KeyanaRepository.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();

        Equal(expected, await ReadScalarAsync(connection, "PRAGMA user_version;"));
        Equal(expected, await ReadScalarAsync(connection, "SELECT value FROM app_meta WHERE name = 'schema_version';"));
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ReinitializeKeepsExistingDataAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var databasePath = Path.Combine(directory, "reopen.db");
        const long minuteStart = 1_800_000_000;
        using (var first = new KeyanaRepository(databasePath))
        {
            await first.InitializeAsync();
            await first.UpsertMinuteBucketAsync(new MinuteBucketSnapshot(minuteStart, NewCounts((KeyId.Z, 4))));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var second = new KeyanaRepository(databasePath);
        await second.InitializeAsync();

        var aggregate = await second.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(4UL, aggregate.Counts[(int)KeyId.Z]);
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static async Task NewerSchemaVersionIsRejectedAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var databasePath = Path.Combine(directory, "future.db");
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version={KeyanaRepository.CurrentSchemaVersion + 1};";
            await command.ExecuteNonQueryAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repository = new KeyanaRepository(databasePath);
        var rejected = false;
        try
        {
            await repository.InitializeAsync();
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Equal(true, rejected);
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static async Task<string> ReadScalarAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string commandText)
{
    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
}

static void PresetRangesUseLocalDayStart()
{
    var zone = FixedZone();
    var localNow = new DateTimeOffset(2026, 11, 16, 15, 30, 45, TimeSpan.FromHours(8));
    var minuteEnd = new DateTimeOffset(2026, 11, 16, 15, 31, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();

    var today = TimeRangeCalculator.ForPreset(TimeRangePreset.Today, localNow, timeZone: zone);
    Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(), today.StartUtc);
    Equal(minuteEnd, today.EndUtc);

    var recent10 = TimeRangeCalculator.ForPreset(TimeRangePreset.Recent10, localNow, timeZone: zone);
    Equal(minuteEnd - 600L, recent10.StartUtc);
    Equal(minuteEnd, recent10.EndUtc);

    var recent7Days = TimeRangeCalculator.ForPreset(TimeRangePreset.Recent7Days, localNow, timeZone: zone);
    Equal(minuteEnd - (7L * 24 * 60 * 60), recent7Days.StartUtc);

    var recent30Days = TimeRangeCalculator.ForPreset(TimeRangePreset.Recent30Days, localNow, timeZone: zone);
    Equal(minuteEnd - (30L * 24 * 60 * 60), recent30Days.StartUtc);
}

static void AllRangeMergesStoredBounds()
{
    var zone = FixedZone();
    var localNow = new DateTimeOffset(2026, 11, 16, 15, 30, 45, TimeSpan.FromHours(8));
    var minuteStart = new DateTimeOffset(2026, 11, 16, 15, 30, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
    var minuteEnd = minuteStart + 60;

    var withoutData = TimeRangeCalculator.ForPreset(TimeRangePreset.All, localNow, timeZone: zone);
    Equal(minuteStart, withoutData.StartUtc);
    Equal(minuteEnd, withoutData.EndUtc);

    var stored = new StoredDataRange(minuteStart - 86_400, minuteStart - 3_600);
    var merged = TimeRangeCalculator.ForPreset(TimeRangePreset.All, localNow, stored, zone);
    Equal(minuteStart - 86_400, merged.StartUtc);
    Equal(minuteEnd, merged.EndUtc);
}

static void CustomRangeSnapsToTenMinutes()
{
    var zone = FixedZone();
    var start = new DateTime(2026, 11, 16, 9, 7, 0, DateTimeKind.Unspecified);
    var end = new DateTime(2026, 11, 16, 10, 3, 0, DateTimeKind.Unspecified);

    var snapped = TimeRangeCalculator.ForCustom(start, end, alignToTenMinutes: true, zone);
    Equal(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(), snapped.StartUtc);
    Equal(new DateTimeOffset(2026, 11, 16, 10, 10, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(), snapped.EndUtc);

    var exact = TimeRangeCalculator.ForCustom(start, end, alignToTenMinutes: false, zone);
    Equal(new DateTimeOffset(2026, 11, 16, 9, 7, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(), exact.StartUtc);
    Equal(new DateTimeOffset(2026, 11, 16, 10, 3, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(), exact.EndUtc);
}

static void YesterdayRangeCoversWholeLocalDay()
{
    var zone = FixedZone();
    var localNow = new DateTimeOffset(2026, 11, 16, 15, 30, 45, TimeSpan.FromHours(8));

    var yesterday = TimeRangeCalculator.ForPreset(TimeRangePreset.Yesterday, localNow, timeZone: zone);
    Equal(
        new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(),
        yesterday.StartUtc);
    Equal(
        new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds(),
        yesterday.EndUtc);

    // 昨天与今天的边界必须首尾相接，不能重叠也不能留空。
    var today = TimeRangeCalculator.ForPreset(TimeRangePreset.Today, localNow, timeZone: zone);
    Equal(today.StartUtc, yesterday.EndUtc);
}

static void WheelAccumulatesToNotches()
{
    var accumulator = new WheelAccumulator();
    Equal(0, accumulator.Add(60));
    Equal(1, accumulator.Add(60));
    Equal(0, accumulator.Add(119));
    Equal(1, accumulator.Add(1));
    Equal(-1, accumulator.Add(-120));
    Equal(0, accumulator.Add(-30));
    Equal(-1, accumulator.Add(-90));
}

static void WheelImpulseCountsRepeatedly()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.RecordImpulse(KeyId.MouseWheelUp).Counted);
    Equal(true, counter.RecordImpulse(KeyId.MouseWheelUp).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.MouseWheelUp]);
    Equal(false, counter.GetSnapshot().PressedKeys.Contains(KeyId.MouseWheelUp));

    counter.SetPaused(true);
    Equal(false, counter.RecordImpulse(KeyId.MouseWheelDown).Counted);
    counter.SetPaused(false);
    Equal(true, counter.RecordImpulse(KeyId.MouseWheelDown).Counted);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.MouseWheelDown]);
}

static async Task DailyUsageMarksActiveAndFireDaysAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        var zone = FixedZone();
        var firstDayLocal = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var firstDayUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(firstDayLocal, zone),
            TimeSpan.Zero).ToUnixTimeSeconds();
        var secondDayUtc = firstDayUtc + 86_400;
        var thirdDayUtc = secondDayUtc + 86_400;

        // 第一天：7999 次，刚好达到火热日。
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(firstDayUtc, NewCounts((KeyId.A, 7999))));
        // 第二天：325 次分布在两分钟，活跃且时长 2 分钟。
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(secondDayUtc, NewCounts((KeyId.B, 200))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(secondDayUtc + 60, NewCounts((KeyId.B, 125))));
        // 第三天：324 次，差一次不算活跃。
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(thirdDayUtc, NewCounts((KeyId.C, 324))));

        var summary = await repository.QueryDailyUsageAsync(firstDayUtc, thirdDayUtc + (2 * 86_400), zone);
        Equal(4, summary.DayCount);
        Equal(1, summary.FireDays);
        Equal(2, summary.ActiveDays);
        Equal(4, summary.TotalActiveMinutes);
        Equal(8648UL, summary.TotalCount);
        Equal(2162d, summary.AveragePerDay);
        Equal(true, summary.Days[0].IsFire);
        Equal(true, summary.Days[1].IsActive);
        Equal(false, summary.Days[2].IsActive);
        Equal(0UL, summary.Days[3].TotalCount);
        Equal(24, summary.Days[0].HourCounts.Count);
        Equal(7999UL, summary.Days[0].HourCounts[0]);
        Equal(0, summary.Days[0].PeakHour);
    });
}

static async Task UiSettingsFallBackToDefaultsAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var settingsPath = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "version": 1,
              "flushIntervalSeconds": 30,
              "heatPalette": "BlueMagenta",
              "theme": "Neon",
              "fontFamily": "   ",
              "fontScale": 9
            }
            """);

        var settings = await new SettingsStore(settingsPath).LoadOrCreateAsync();
        Equal("System", settings.Theme);
        Equal(1.0, settings.FontScale);
        Equal(string.Empty, settings.FontFamily);
        Equal(true, settings.SnapToTenMinutes);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}



static TimeZoneInfo FixedZone() =>
    TimeZoneInfo.CreateCustomTimeZone("Keyana.Tests+08", TimeSpan.FromHours(8), "Tests+08", "Tests+08");

static async Task WithRepositoryAsync(Func<string, KeyanaRepository, Task> test)
{
    var directory = CreateTestDirectory();
    try
    {
        using var repository = new KeyanaRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        await test(directory, repository);
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static string CreateTestDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "Keyana.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static uint[] NewCounts(params (KeyId Key, uint Count)[] values)
{
    var counts = new uint[KeyCatalog.Count];
    foreach (var (key, count) in values)
    {
        counts[(int)key] = count;
    }

    return counts;
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"没有找到期望文本：{expected}");
    }
}

static KeyId? Normalize(ushort scanCode, ushort virtualKey, RawKeyFlags flags = RawKeyFlags.None) =>
    KeyNormalizer.Normalize(new RawKeyEvent(scanCode, flags, virtualKey));

static RawKeyEvent MakeA() => new(0x1E, RawKeyFlags.None, 0x41);
static RawKeyEvent BreakA() => new(0x1E, RawKeyFlags.Break, 0x41);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}

file sealed class ManualClock(DateTimeOffset utcNow) : IUtcClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
