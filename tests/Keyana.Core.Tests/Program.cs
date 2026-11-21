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
    ("设备路径提取硬件标识", Sync(DevicePathExtractsHardwareId)),
    ("设备路径缺少硬件标识时退回类名", Sync(DevicePathFallsBackToClass)),
    ("空设备路径降级为未知", Sync(EmptyDevicePathDegrades)),
    ("未识别日志按五列输出", Sync(UnrecognizedLogLineKeepsColumns)),
    ("未识别日志拍平制表符并兜底空来源", Sync(UnrecognizedLogLineFlattensAndFallsBack)),
    ("未识别日志只在打开时写入并轮转", Sync(UnrecognizedLogWritesOnlyWhenEnabledAndRotates)),
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
    ("时段分钟数据按一天中的位置累加", TimelineMinuteTotalsAggregateByTimeOfDayAsync),
    ("同一分钟重启后继续累计", RecorderResumesCurrentMinuteAsync),
    ("保存瞬时失败会退避重试并最终落盘", RecorderRetriesTransientFailureAsync),
    ("持续保存失败不丢内存数据，恢复后补写", RecorderKeepsDataAfterPersistentFailureAsync),
    ("退出期间接受的输入全部落盘", RecorderPersistsAcceptedPressesOnDisposeAsync),
    ("手动保存被取消不影响后续落盘", RecorderFlushCancellationDoesNotLoseDataAsync),
    ("持续失败时退出仍能完成清理", RecorderDisposeCompletesWhenSaveKeepsFailingAsync),
    ("慢写下不会用旧快照覆盖新数据", RecorderNeverWritesStaleSnapshotAsync),
    ("退出后拒绝新的按键", RecorderRejectsPressesAfterDisposeAsync),
    ("旧数据库迁移先把 WAL 并回主库", LegacyMigrationMergesWalBeforeMoveAsync),
    ("旧数据库被占用时拒绝迁移", LegacyMigrationRefusesWhileLockedAsync),
    ("设置文件语法损坏时降级为默认值", CorruptSettingsFallBackToDefaultsAsync),
    ("CSV 导出包含分钟与键位", CsvExportContainsMinuteRowsAsync),
    ("设置文件可原子创建和读取", SettingsStoreRoundTripsAsync),
    ("旧设置文件含未知字段仍可读取", LegacySettingsWithUnknownKeysStillLoadsAsync),
    ("预设范围按本地零点与固定长度计算", Sync(PresetRangesUseLocalDayStart)),
    ("昨天范围覆盖完整本地自然日", Sync(YesterdayRangeCoversWholeLocalDay)),
    ("全部范围并入历史数据边界", Sync(AllRangeMergesStoredBounds)),
    ("自定义范围按十分钟向外对齐", Sync(CustomRangeSnapsToTenMinutes)),
    ("自定义区间不能走预设换算", Sync(CustomPresetRequiresExplicitRange)),
    ("午夜发生夏令时跳变时仍能算出当天起点", Sync(MidnightDstTransitionUsesFirstValidInstant)),
    ("区间名称统一用「日」", Sync(RangeLabelsUseDayCharacter)),
    ("「全部」区间名称带出实际天数", Sync(AllRangeLabelCarriesDayCount)),
    ("滚轮手势窗口内的连续滚动只算一次", Sync(WheelGestureMergesWithinWindow)),
    ("滚轮手势上下方向各自独立计时", Sync(WheelDirectionsTimeIndependently)),
    ("滚轮零增量不计数也不推进时间", Sync(WheelZeroDeltaIsIgnored)),
    ("滚轮手势 Reset 后重新计数", Sync(WheelResetStartsFreshGesture)),
    ("暂停期间滚轮手势不计数", Sync(WheelGestureNotCountedWhilePaused)),
    ("滚轮距离与手势判定互不干扰", Sync(WheelDistanceAndGestureAreIndependent)),
    ("滚轮距离按 120 折算并估算长度", Sync(WheelDistanceConvertsToNotchesAndCentimeters)),
    ("手势脉冲可连续计数且不进入按下状态", Sync(WheelImpulseCountsRepeatedly)),
    ("键位表 v6 可往返且旧数据新键位为 0", Sync(CountVectorV6RoundTrips)),
    ("滚轮距离随分钟桶一起落库", WheelDistanceStagesWithMinuteBucketAsync),
    ("数据库 v1 升级到 v2 保留旧数据", SchemaV1UpgradesToV2Async),
    ("滚轮设置坏值时降级为默认", Sync(WheelSettingsFallBackToDefaults)),
    ("逐日汇总识别活跃日、火热日与逐小时分布", DailyUsageMarksActiveAndFireDaysAsync),
    ("界面设置损坏时降级为默认值", UiSettingsFallBackToDefaultsAsync),
    ("Release 构建只显示数字版本号", Sync(ReleaseBuildShowsNumericVersion)),
    ("Release 构建剥离版本号里的 -dev", Sync(ReleaseBuildStripsDevelopmentSuffix)),
    ("Development 构建不重复追加 -dev", Sync(DevelopmentBuildDoesNotDoubleDevSuffix)),
    ("Development 构建版本号带 -dev", Sync(DevelopmentBuildAppendsDevSuffix)),
    ("只有 Development 构建展示 Build ID", Sync(BuildIdIsShownOnlyInDevelopment)),
    ("-dev 后缀与工作区是否干净无关", Sync(DevSuffixIsIndependentOfGitStatus)),
    ("预发布版本保留既有渠道标识", Sync(PrereleaseTagIsKept)),
    ("缺少 Git 信息时构建信息降级", Sync(MissingGitInfoIsDegraded)),
    ("非哈希的 Git 输出降级为 unknown", Sync(NonHashGitOutputFallsBackToUnknown)),
    ("构建信息诊断文本包含全部字段", Sync(DiagnosticTextListsEveryField)),
    ("键盘着色基准不受鼠标量级影响", Sync(KeyboardBasisIgnoresMouse)),
    ("鼠标着色基准只看鼠标按键", Sync(MouseBasisIgnoresKeyboard)),
    ("合计着色基准取全局最大键", Sync(CombinedBasisUsesGlobalMaximum)),
    ("着色基准解析失败时降级", Sync(ColorBasisParseFallsBack)),
    ("未筛选时直接复用原计数", Sync(KeyFilterWithoutSelectionReusesCounts)),
    ("筛选只保留选中键的计数", Sync(KeyFilterKeepsOnlySelectedKeys)),
    ("内置配列都能通过校验", Sync(BuiltInLayoutsAreValid)),
    ("内置配列的键数符合名称", Sync(BuiltInLayoutKeyCountsMatchNames)),
    ("内置配列不包含鼠标按键", Sync(BuiltInLayoutsExcludeMouseKeys)),
    ("配列 JSON 往返保持一致", Sync(LayoutJsonRoundTrips)),
    ("重叠的键帽会被拒绝", Sync(OverlappingLayoutIsRejected)),
    ("未知按键名会被拒绝", Sync(UnknownLayoutKeyIsRejected)),
    ("不支持的配列版本会被拒绝", Sync(UnsupportedLayoutVersionIsRejected)),
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

static void DevicePathExtractsHardwareId()
{
    const string path = @"\\?\HID#VID_046D&PID_C52B&MI_00#8&1a2b3c4d&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
    Equal("VID_046D&PID_C52B", DevicePathLabel.FromPath(path));
    const string lower = @"\\?\hid#vid_046d&pid_c52b&mi_00#8&1a2b3c4d&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
    Equal("VID_046D&PID_C52B", DevicePathLabel.FromPath(lower));
    // 只有 VID 没有 PID 时不要拼出半截标识。
    Equal("VID_046D", DevicePathLabel.FromPath(@"\\?\HID#VID_046D#7&abc&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"));
}

static void DevicePathFallsBackToClass()
{
    Equal(@"ROOT\RDP_KBD", DevicePathLabel.FromPath(@"\\?\ROOT#RDP_KBD#0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"));
    Equal(@"ACPI\VEN_PNP&DEV_0303", DevicePathLabel.FromPath(@"\\?\ACPI#VEN_PNP&DEV_0303#4&1a2b3c4d&0#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"));
}

static void EmptyDevicePathDegrades()
{
    Equal("未知设备", DevicePathLabel.FromPath(null));
    Equal("未知设备", DevicePathLabel.FromPath(""));
    Equal("未知设备", DevicePathLabel.FromPath("   "));
}

static void UnrecognizedLogLineKeepsColumns()
{
    var at = new DateTimeOffset(2026, 11, 21, 11, 5, 6, 123, TimeSpan.FromHours(8));
    var line = new UnrecognizedInputRecord(at, 0x00, 0x25, 0x00, @"ROOT\FEIZHI_VIRTUAL_KEYBOARD").ToLogLine();
    var columns = line.Split('\t');
    Equal(5, columns.Length);
    Equal("2026-11-21 11:05:06.123 +08:00", columns[0]);
    Equal("0x00", columns[1]);
    Equal("0x25", columns[2]);
    Equal("0x00", columns[3]);
    Equal(@"ROOT\FEIZHI_VIRTUAL_KEYBOARD", columns[4]);
    // 表头必须和数据同列数，否则拖进 Excel 会整列错位。
    Equal(5, UnrecognizedInputRecord.HeaderLine.Split('\t').Length);
}

static void UnrecognizedLogLineFlattensAndFallsBack()
{
    var epoch = DateTimeOffset.UnixEpoch;
    var flattened = new UnrecognizedInputRecord(epoch, 0, 0, 0, "A\tB\nC").ToLogLine();
    Equal(5, flattened.Split('\t').Length);
    Equal("A B C", flattened.Split('\t')[4]);
    Equal("未知设备", new UnrecognizedInputRecord(epoch, 0, 0, 0, "   ").ToLogLine().Split('\t')[4]);
}

static void UnrecognizedLogWritesOnlyWhenEnabledAndRotates()
{
    var directory = Directory.CreateTempSubdirectory("keyana-unrecognized-");
    try
    {
        var path = Path.Combine(directory.FullName, "unrecognized.log");
        var log = new UnrecognizedInputLog(path, maxBytes: 200);
        var record = new UnrecognizedInputRecord(
            new DateTimeOffset(2026, 11, 21, 11, 5, 6, TimeSpan.FromHours(8)), 0x00, 0x25, 0x00, "A");

        // 关着的时候一个字节都不写。
        log.Append(record);
        Equal(false, File.Exists(path));

        log.Enabled = true;
        log.Append(record);
        log.Append(record);
        var lines = File.ReadAllLines(path);
        Equal(3, lines.Length);
        Equal(UnrecognizedInputRecord.HeaderLine, lines[0]);
        Equal(5, lines[1].Split('\t').Length);

        // 继续写，超过 200 字节就轮转；新文件重新写一次表头。
        for (var index = 0; index < 5; index++)
        {
            log.Append(record);
        }

        Equal(true, File.Exists(path + ".1"));
        Equal(UnrecognizedInputRecord.HeaderLine, File.ReadAllLines(path)[0]);
    }
    finally
    {
        directory.Delete(recursive: true);
    }
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

static async Task TimelineMinuteTotalsAggregateByTimeOfDayAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long baseTime = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime, NewCounts((KeyId.A, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 60, NewCounts((KeyId.B, 3))));
        // 第二天的同一分钟：应当累加到「一天中的同一位置」，而不是变成第 1441 个点。
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 86_400, NewCounts((KeyId.C, 5))));

        var totals = await repository.QueryTimeOfDayMinuteTotalsAsync(baseTime, baseTime + 86_400 + 60);

        Equal(1440, totals.Length);
        Equal(7UL, totals[LocalMinuteIndex(baseTime)]);
        Equal(3UL, totals[LocalMinuteIndex(baseTime + 60)]);
    });
}

static int LocalMinuteIndex(long utcSeconds)
{
    var local = DateTimeOffset.FromUnixTimeSeconds(utcSeconds).ToLocalTime();
    return (local.Hour * 60) + local.Minute;
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

static async Task RecorderRetriesTransientFailureAsync()
{
    var store = new FakeMinuteStore { FailuresRemaining = 2 };
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    await using var recorder = await StatisticsRecorder.CreateAsync(
        store,
        TimeSpan.FromHours(1),
        clock);

    recorder.RecordKeyPress(KeyId.A);
    recorder.RecordKeyPress(KeyId.A);
    recorder.RecordKeyPress(KeyId.B);

    // 前两次写入抛异常，第三次尝试成功——FlushNow 应该正常返回而不是把错误抛给界面。
    await recorder.FlushNowAsync();

    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    Equal(3L, store.TotalFor(minuteStart));
    Equal(true, recorder.LastError is null);
    Equal(1, store.WriteCountFor(minuteStart));
}

static async Task RecorderKeepsDataAfterPersistentFailureAsync()
{
    var store = new FakeMinuteStore { AlwaysFail = true };
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    await using var recorder = await StatisticsRecorder.CreateAsync(
        store,
        TimeSpan.FromHours(1),
        clock);

    recorder.RecordKeyPress(KeyId.A);
    recorder.RecordKeyPress(KeyId.A);

    var failure = await CaptureAsync(() => recorder.FlushNowAsync());
    Equal(true, failure is InvalidOperationException);
    Equal(true, recorder.LastError is { Length: > 0 });

    // 关键点：失败后计数仍留在待写表里，恢复后能补写，不会被“内存已清空”吞掉。
    store.AlwaysFail = false;
    await recorder.FlushNowAsync();

    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    Equal(2L, store.TotalFor(minuteStart));
    Equal(true, recorder.LastError is null);
}

static async Task RecorderPersistsAcceptedPressesOnDisposeAsync()
{
    var store = new FakeMinuteStore();
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    var recorder = await StatisticsRecorder.CreateAsync(store, TimeSpan.FromHours(1), clock);

    var accepted = 0;
    var stop = 0;
    var presser = Task.Run(() =>
    {
        while (Volatile.Read(ref stop) == 0)
        {
            if (recorder.RecordKeyPress(KeyId.A))
            {
                Interlocked.Increment(ref accepted);
            }
        }
    });

    await Task.Delay(30);
    await recorder.DisposeAsync();
    Volatile.Write(ref stop, 1);
    await presser;

    // 退出流程只允许两种结果：接受并保存，或明确拒绝。绝不接受却永不落盘。
    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    Equal((long)accepted, store.TotalFor(minuteStart));
    Equal(false, recorder.RecordKeyPress(KeyId.A));
}

static async Task RecorderFlushCancellationDoesNotLoseDataAsync()
{
    var store = new FakeMinuteStore { WriteDelay = TimeSpan.FromMilliseconds(60) };
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    await using var recorder = await StatisticsRecorder.CreateAsync(
        store,
        TimeSpan.FromHours(1),
        clock);

    recorder.RecordKeyPress(KeyId.A);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    var failure = await CaptureAsync(() => recorder.FlushNowAsync(cancellation.Token));
    Equal(true, failure is OperationCanceledException);

    // 取消只影响这次等待，快照仍会写下去；再 flush 一次确保等到写入完成。
    await recorder.FlushNowAsync();
    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    Equal(1L, store.TotalFor(minuteStart));
}

static async Task RecorderDisposeCompletesWhenSaveKeepsFailingAsync()
{
    var store = new FakeMinuteStore { AlwaysFail = true };
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    var recorder = await StatisticsRecorder.CreateAsync(store, TimeSpan.FromHours(1), clock);
    recorder.RecordKeyPress(KeyId.A);

    var started = System.Diagnostics.Stopwatch.StartNew();
    await recorder.DisposeAsync();
    started.Stop();

    // 关键：写库一直失败时 Dispose 也必须返回并清完定时器/队列，不能挂住退出流程。
    Equal(true, started.Elapsed < TimeSpan.FromSeconds(10));
    Equal(true, recorder.LastError is { Length: > 0 });
    Equal(false, recorder.RecordKeyPress(KeyId.A));
}

static async Task RecorderNeverWritesStaleSnapshotAsync()
{
    var store = new FakeMinuteStore { WriteDelay = TimeSpan.FromMilliseconds(40) };
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    await using var recorder = await StatisticsRecorder.CreateAsync(
        store,
        TimeSpan.FromHours(1),
        clock);

    recorder.RecordKeyPress(KeyId.A);
    var firstFlush = recorder.FlushNowAsync();

    // 第一次写入还在“磁盘”上时继续按键，制造新旧快照重叠。
    await Task.Delay(10);
    for (var index = 0; index < 10; index++)
    {
        recorder.RecordKeyPress(KeyId.B);
    }

    var secondFlush = recorder.FlushNowAsync();
    await Task.WhenAll(firstFlush, secondFlush);
    await recorder.DisposeAsync();

    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    var totals = store.WriteTotalsFor(minuteStart);
    Equal(true, totals.Count >= 2);
    for (var index = 1; index < totals.Count; index++)
    {
        // 绝对快照按桶合并后，后写的一定不比先写的小。
        Equal(true, totals[index] >= totals[index - 1]);
    }

    Equal(11L, totals[^1]);
    Equal(11L, store.TotalFor(minuteStart));
}

static async Task RecorderRejectsPressesAfterDisposeAsync()
{
    var store = new FakeMinuteStore();
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    var recorder = await StatisticsRecorder.CreateAsync(store, TimeSpan.FromHours(1), clock);
    recorder.RecordKeyPress(KeyId.A);

    await recorder.DisposeAsync();

    Equal(false, recorder.RecordKeyPress(KeyId.A));
    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    Equal(1L, store.TotalFor(minuteStart));
}

static async Task LegacyMigrationMergesWalBeforeMoveAsync()
{
    var sourceDirectory = CreateTestDirectory();
    var destinationDirectory = CreateTestDirectory();
    try
    {
        // 造一个「主库文件里没有、数据只在 WAL 中」的旧库：写入后不 checkpoint，
        // 连接还开着时把 db + wal 复制走（等价于异常退出后留下的现场）。
        var sourcePath = Path.Combine(sourceDirectory, "key-stats.db");
        var sourceConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Pooling = false,
        }.ToString();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(sourceConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
            await ExecuteAsync(connection, "PRAGMA wal_autocheckpoint=0;");
            await ExecuteAsync(connection, "CREATE TABLE marker(value TEXT);");
            await ExecuteAsync(connection, "INSERT INTO marker(value) VALUES('kept');");
            CopyDatabaseFiles(sourceDirectory, "key-stats.db", destinationDirectory, "key-stats.db");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var legacyPath = Path.Combine(destinationDirectory, "key-stats.db");
        var walPath = legacyPath + "-wal";
        Equal(true, File.Exists(walPath));
        Equal(true, new FileInfo(walPath).Length > 0);

        var targetPath = Path.Combine(destinationDirectory, "keyana.db");
        Equal(true, LegacyDatabaseMigrator.TryMigrate(legacyPath, targetPath, out var error));
        Equal(true, error is null);
        Equal(false, File.Exists(legacyPath));
        Equal(true, File.Exists(targetPath));

        await using var verify = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = targetPath,
                Pooling = false,
            }.ToString());
        await verify.OpenAsync();
        Equal("kept", await ReadScalarAsync(verify, "SELECT value FROM marker;"));
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(sourceDirectory, recursive: true);
        Directory.Delete(destinationDirectory, recursive: true);
    }
}

static async Task LegacyMigrationRefusesWhileLockedAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var legacyPath = Path.Combine(directory, "key-stats.db");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = legacyPath,
            Pooling = false,
        }.ToString();

        await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await setup.OpenAsync();
            await ExecuteAsync(setup, "PRAGMA journal_mode=WAL;");
            await ExecuteAsync(setup, "CREATE TABLE marker(value TEXT);");
            await ExecuteAsync(setup, "INSERT INTO marker(value) VALUES('kept');");
        }

        var targetPath = Path.Combine(directory, "keyana.db");
        await using (var locker = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await locker.OpenAsync();
            await ExecuteAsync(locker, "BEGIN IMMEDIATE;");
            await ExecuteAsync(locker, "INSERT INTO marker(value) VALUES('pending');");

            // 有活动事务时 checkpoint 无法完成，迁移必须放弃并把旧库留在原处。
            Equal(false, LegacyDatabaseMigrator.TryMigrate(legacyPath, targetPath, out var error));
            Equal(true, error is { Length: > 0 });
            Equal(false, File.Exists(targetPath));
            Equal(true, File.Exists(legacyPath));
            await ExecuteAsync(locker, "ROLLBACK;");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Equal(true, LegacyDatabaseMigrator.TryMigrate(legacyPath, targetPath, out var secondError));
        Equal(true, secondError is null);
        Equal(true, File.Exists(targetPath));
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static async Task CorruptSettingsFallBackToDefaultsAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var settingsPath = Path.Combine(directory, "settings.json");
        const string brokenJson = "{ \"version\": 1, \"flushIntervalSeconds\": ";
        await File.WriteAllTextAsync(settingsPath, brokenJson);

        var store = new SettingsStore(settingsPath);
        var settings = await store.LoadOrCreateAsync();

        // 设置坏掉只影响外观；数据库初始化必须继续（旧实现会在这里抛出，
        // 导致整个会话“在采集但不入库”）。损坏文件保持原样，便于用户排查。
        Equal(30, settings.FlushIntervalSeconds);
        Equal("System", settings.Theme);
        Equal(true, store.LastLoadWarning is { Length: > 0 });
        Equal(brokenJson, await File.ReadAllTextAsync(settingsPath));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ExecuteAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static void CopyDatabaseFiles(
    string sourceDirectory,
    string sourceName,
    string destinationDirectory,
    string destinationName)
{
    // -shm 是缓存，SQLite 会按 WAL 重建；真正必须带走的是主库与 -wal。
    foreach (var suffix in new[] { string.Empty, "-wal" })
    {
        var source = Path.Combine(sourceDirectory, sourceName + suffix);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(destinationDirectory, destinationName + suffix), overwrite: true);
        }
    }
}

static async Task<Exception?> CaptureAsync(Func<Task> action)
{
    try
    {
        await action();
        return null;
    }
    catch (Exception exception)
    {
        return exception;
    }
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

static void CustomPresetRequiresExplicitRange()
{
    var zone = FixedZone();
    var threw = false;
    try
    {
        TimeRangeCalculator.ForPreset(TimeRangePreset.Custom, DateTimeOffset.Now, timeZone: zone);
    }
    catch (ArgumentException)
    {
        threw = true;
    }

    Equal(true, threw);
}

static void MidnightDstTransitionUsesFirstValidInstant()
{
    // 造一个在本地 00:00 直接跳到 01:00 的时区（部分时区历史上有过这种规则）。
    var daylightStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
        new DateTime(1, 1, 1, 0, 0, 0),
        month: 3,
        day: 8);
    var daylightEnd = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
        new DateTime(1, 1, 1, 0, 0, 0),
        month: 11,
        day: 1);
    var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
        new DateTime(2026, 1, 1),
        new DateTime(2026, 12, 31),
        TimeSpan.FromHours(1),
        daylightStart,
        daylightEnd);
    var zone = TimeZoneInfo.CreateCustomTimeZone(
        "Keyana.Tests.MidnightDst",
        TimeSpan.FromHours(8),
        "Tests +08 with midnight DST",
        "Tests Standard",
        "Tests Daylight",
        [rule]);

    Equal(true, zone.IsInvalidTime(new DateTime(2026, 3, 8, 0, 0, 0)));

    // 当天 00:00 不存在，起点应落到第一个有效时刻 01:00（+09:00），而不是抛异常。
    var localNow = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.FromHours(9));
    var today = TimeRangeCalculator.ForPreset(TimeRangePreset.Today, localNow, timeZone: zone);
    Equal(
        new DateTimeOffset(2026, 3, 7, 16, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        today.StartUtc);
}

static void RangeLabelsUseDayCharacter()
{
    // 范围写「日」，与「活跃天数」「共 87 天」这类数量说法分开。
    Equal("今日", TimeRangeLabels.ForPreset(TimeRangePreset.Today));
    Equal("昨日", TimeRangeLabels.ForPreset(TimeRangePreset.Yesterday));
    Equal("最近 10 分钟", TimeRangeLabels.ForPreset(TimeRangePreset.Recent10));
    Equal("近 7 日", TimeRangeLabels.ForPreset(TimeRangePreset.Recent7Days));
    Equal("近 30 日", TimeRangeLabels.ForPreset(TimeRangePreset.Recent30Days));
    Equal("近 90 日", TimeRangeLabels.ForPreset(TimeRangePreset.Recent90Days));
    Equal("自定义区间", TimeRangeLabels.ForPreset(TimeRangePreset.Custom));

    // 任何一个区间名里都不能再出现「天」，否则两套说法又会并存。
    foreach (var preset in Enum.GetValues<TimeRangePreset>())
    {
        var label = TimeRangeLabels.ForPreset(preset, 87);
        if (label.Contains('天', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"区间名里出现了「天」：{label}");
        }
    }
}

static void AllRangeLabelCarriesDayCount()
{
    // 「全部」没有固定跨度，把实际覆盖的自然日数写进名字里。
    Equal("全部87日", TimeRangeLabels.ForPreset(TimeRangePreset.All, 87));
    // 天数未知时退回只写「全部」，不能出现「全部 0 日」。
    Equal("全部", TimeRangeLabels.ForPreset(TimeRangePreset.All, 0));
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

/// <summary>
/// 按采集层的真实顺序喂事件：判定器 -> 新手势才记一次脉冲。
/// 返回本次调用新记的次数。距离由调用方另算，两者在实现里就是互不依赖的。
/// </summary>
static int FeedWheel(
    KeyboardCounter counter,
    WheelGestureDetector detector,
    nint deviceHandle,
    int windowMs,
    params (int Delta, long Ticks)[] events)
{
    var counted = 0;
    foreach (var (delta, ticks) in events)
    {
        var result = detector.Observe(deviceHandle, delta, ticks, windowMs);
        if (!result.Accepted || !result.IsNewGesture)
        {
            continue;
        }

        var keyId = result.Direction == WheelDirection.Up
            ? KeyId.WheelGestureUp
            : KeyId.WheelGestureDown;
        if (counter.RecordImpulse(keyId).Counted)
        {
            counted++;
        }
    }

    return counted;
}

static long CountOf(KeyboardCounter counter, KeyId keyId) => counter.GetSnapshot().Counts[(int)keyId];

static void WheelGestureMergesWithinWindow()
{
    var counter = new KeyboardCounter();
    var detector = new WheelGestureDetector();

    // 窗口 200ms：三条间隔 50ms 的上滑是「一次动作」，不是三次。
    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 0), (240, 50), (120, 100)));
    Equal(1L, CountOf(counter, KeyId.WheelGestureUp));

    // 距上一次事件 300ms > 200ms，才记第二次。
    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 400)));
    Equal(2L, CountOf(counter, KeyId.WheelGestureUp));

    // 换一台设备不会被上一台的时间戳吞掉。
    Equal(1, FeedWheel(counter, detector, 2, 200, (120, 450)));
    Equal(3L, CountOf(counter, KeyId.WheelGestureUp));
}

static void WheelDirectionsTimeIndependently()
{
    var counter = new KeyboardCounter();
    var detector = new WheelGestureDetector();

    // 上 -> 下 -> 上，三次都在窗口内。用一个全局时间戳会被压成一次；
    // 上下分流后是「上 1 次 + 下 1 次」，第三次上滑并入第一次。
    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 0)));
    Equal(1, FeedWheel(counter, detector, 1, 200, (-120, 50)));
    Equal(0, FeedWheel(counter, detector, 1, 200, (120, 100)));

    Equal(1L, CountOf(counter, KeyId.WheelGestureUp));
    Equal(1L, CountOf(counter, KeyId.WheelGestureDown));
    Equal(2L, counter.GetSnapshot().TotalCount);
}

static void WheelZeroDeltaIsIgnored()
{
    var counter = new KeyboardCounter();
    var detector = new WheelGestureDetector();

    var ignored = detector.Observe(1, 0, 1000, 200);
    Equal(false, ignored.Accepted);
    Equal(false, ignored.IsNewGesture);

    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 1000)));

    // 零增量夹在中间：如果它推进了时间戳，下一条 1300 会被并进上一次；
    // 没有推进则距 1000 已 300ms > 200ms，算第二次。
    Equal(0, FeedWheel(counter, detector, 1, 200, (0, 1200)));
    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 1300)));
    Equal(2L, CountOf(counter, KeyId.WheelGestureUp));
}

static void WheelResetStartsFreshGesture()
{
    var detector = new WheelGestureDetector();

    Equal(true, detector.Observe(1, 120, 1000, 200).IsNewGesture);
    Equal(false, detector.Observe(1, 120, 1100, 200).IsNewGesture);

    // 暂停恢复 / 校准结束：陈旧的 1100 不能继续压着新手势。
    detector.Reset();
    Equal(true, detector.Observe(1, 120, 1150, 200).IsNewGesture);

    // 定向清理只影响那一台设备：清一台不存在的设备不能把别的设备清掉。
    Equal(false, detector.Observe(1, 120, 1200, 200).IsNewGesture);
    Equal(true, detector.Observe(7, 120, 1300, 200).IsNewGesture);
    detector.Reset(9);
    Equal(false, detector.Observe(1, 120, 1310, 200).IsNewGesture);
    detector.Reset(1);
    Equal(true, detector.Observe(1, 120, 1320, 200).IsNewGesture);
}

static void WheelGestureNotCountedWhilePaused()
{
    var counter = new KeyboardCounter();
    var detector = new WheelGestureDetector();

    counter.SetPaused(true);
    // 判定照常给出「新手势」，但暂停期间不落计数（与键盘、鼠标键一致）。
    Equal(0, FeedWheel(counter, detector, 1, 200, (120, 0), (120, 500)));
    Equal(0L, CountOf(counter, KeyId.WheelGestureUp));

    counter.SetPaused(false);
    Equal(1, FeedWheel(counter, detector, 1, 200, (120, 1000)));
    Equal(1L, CountOf(counter, KeyId.WheelGestureUp));
}

static void WheelDistanceAndGestureAreIndependent()
{
    var counter = new KeyboardCounter();
    var detector = new WheelGestureDetector();
    long up = 0;
    long down = 0;

    // 同一串原始事件同时产出两个指标：距离逐条累加（不做 120 折算），
    // 手势只在静默超过窗口时才 +1。
    (int Delta, long Ticks)[] events =
    [
        (120, 0), (120, 16), (240, 32), (120, 64),
        (-120, 80), (-240, 96),
        (120, 800),
    ];

    var gestures = 0;
    foreach (var (delta, ticks) in events)
    {
        var result = detector.Observe(1, delta, ticks, 200);
        if (delta > 0)
        {
            up += delta;
        }
        else
        {
            down -= delta;
        }

        if (result is { Accepted: true, IsNewGesture: true })
        {
            gestures++;
        }
    }

    Equal(720L, up);
    Equal(360L, down);
    // 上滑两次（0 与 800 那两簇），下滑一次。
    Equal(3, gestures);
    Equal(0L, counter.GetSnapshot().TotalCount);

    // 距离不参与次数：一格里 120 单位的 delta 不会被折算成「1 次」。
    Equal(0L, CountOf(counter, KeyId.MouseWheelUp));
    Equal(0L, CountOf(counter, KeyId.MouseWheelDown));
}

static void WheelDistanceConvertsToNotchesAndCentimeters()
{
    Equal(12d, WheelMetrics.Notches(1440));
    Equal(0d, WheelMetrics.Notches(0));

    // 一格 ≈ 60 DIP，96 DPI 基准下约 1.59 cm；系数调大表示「一次滚得更多行」，估算更长。
    var centimeters = WheelMetrics.EstimateCentimeters(120, 1.0);
    Equal(true, Math.Abs(centimeters - 1.5875) < 0.001);
    Equal(true, WheelMetrics.EstimateCentimeters(120, 1.25) < centimeters);
    // 非法系数不产生 NaN / 负数。
    Equal(true, WheelMetrics.EstimateCentimeters(120, double.NaN) > 0);
    Equal(true, WheelMetrics.EstimateCentimeters(120, 0) > 0);

    Equal(true, WheelMetrics.IsDistanceKey(KeyId.MouseWheelUp));
    Equal(true, WheelMetrics.IsGestureKey(KeyId.WheelGestureDown));
    Equal(false, WheelMetrics.IsGestureKey(KeyId.MouseWheelUp));
}

static void WheelImpulseCountsRepeatedly()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.RecordImpulse(KeyId.WheelGestureUp).Counted);
    Equal(true, counter.RecordImpulse(KeyId.WheelGestureUp).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.WheelGestureUp]);
    Equal(false, counter.GetSnapshot().PressedKeys.Contains(KeyId.WheelGestureUp));

    counter.SetPaused(true);
    Equal(false, counter.RecordImpulse(KeyId.WheelGestureDown).Counted);
    counter.SetPaused(false);
    Equal(true, counter.RecordImpulse(KeyId.WheelGestureDown).Counted);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.WheelGestureDown]);
}

static void CountVectorV6RoundTrips()
{
    Equal(6, CountVectorCodec.KeyMapVersion);

    // 新键位必须追加在末尾：下标一旦前移，历史数据的每一列都会错位。
    Equal((int)KeyId.MouseXButton2 + 1, (int)KeyId.WheelGestureUp);
    Equal((int)KeyId.WheelGestureUp + 1, (int)KeyId.WheelGestureDown);
    Equal((int)KeyId.WheelGestureDown, KeyCatalog.Count - 1);

    var counts = new uint[KeyCatalog.Count];
    counts[(int)KeyId.WheelGestureUp] = 7;
    counts[(int)KeyId.WheelGestureDown] = 9;
    counts[(int)KeyId.MouseWheelUp] = 0;
    var decoded = CountVectorCodec.Decode(CountVectorCodec.Encode(counts));
    Equal(counts.Length, decoded.Length);
    Equal(7u, decoded[(int)KeyId.WheelGestureUp]);
    Equal(9u, decoded[(int)KeyId.WheelGestureDown]);

    // v5 及更早的数据只写到 MouseWheelDown：新键位必须解成 0，不能越界或抛错。
    var legacy = new uint[KeyCatalog.Count];
    legacy[(int)KeyId.MouseWheelDown] = 5;
    var legacyDecoded = CountVectorCodec.Decode(CountVectorCodec.Encode(legacy));
    Equal(5u, legacyDecoded[(int)KeyId.MouseWheelDown]);
    Equal(0u, legacyDecoded[(int)KeyId.WheelGestureUp]);
    Equal(0u, legacyDecoded[(int)KeyId.WheelGestureDown]);
}

static async Task WheelDistanceStagesWithMinuteBucketAsync()
{
    var store = new FakeMinuteStore();
    var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
    await using var recorder = await StatisticsRecorder.CreateAsync(
        store,
        TimeSpan.FromHours(1),
        clock);

    Equal(true, recorder.RecordWheelDistance(up: true, delta: 120));
    Equal(true, recorder.RecordWheelDistance(up: true, delta: 240));
    Equal(true, recorder.RecordWheelDistance(up: false, delta: -120));

    var live = recorder.GetCurrentSnapshot();
    // 距离不进次数向量，也不进 TotalCount。
    Equal(0L, live.TotalCount);
    Equal(360L, live.WheelDistanceUp);
    Equal(120L, live.WheelDistanceDown);

    await recorder.FlushNowAsync();

    var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
    var stored = store.DistancesFor(minuteStart);
    Equal(360L, stored.Up);
    Equal(120L, stored.Down);
    // 只有距离、没有按键的一分钟也必须落库，不能当成空桶丢掉。
    Equal(0L, store.TotalFor(minuteStart));
    var storedBucket = await store.GetMinuteBucketAsync(minuteStart);
    Equal(0u, storedBucket!.Counts[(int)KeyId.WheelGestureUp]);
}

static async Task SchemaV1UpgradesToV2Async()
{
    var directory = CreateTestDirectory();
    var databasePath = Path.Combine(directory, "upgrade.db");
    const long minuteStart = 1_800_000_000;
    var blob = CountVectorCodec.Encode(NewCounts((KeyId.A, 4), (KeyId.Escape, 2)));

    try
    {
        // 手工造一个 v1 库：与当年的建表语句一致，没有距离列。
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE minute_buckets (
                    bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 60 = 0),
                    keymap_version INTEGER NOT NULL,
                    counts BLOB NOT NULL,
                    total_count INTEGER NOT NULL CHECK(total_count >= 0),
                    updated_at_utc INTEGER NOT NULL
                ) WITHOUT ROWID;
                CREATE TABLE rollup_10m (
                    bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 600 = 0),
                    keymap_version INTEGER NOT NULL,
                    counts BLOB NOT NULL,
                    total_count INTEGER NOT NULL CHECK(total_count >= 0)
                ) WITHOUT ROWID;
                CREATE TABLE app_meta (
                    name TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                ) WITHOUT ROWID;
                INSERT INTO app_meta(name, value) VALUES('schema_version', '1');
                PRAGMA user_version=1;
                """;
            await command.ExecuteNonQueryAsync();

            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO minute_buckets (
                    bucket_start_utc, keymap_version, counts, total_count, updated_at_utc)
                VALUES ($start, 5, $counts, 6, $start);
                """;
            insert.Parameters.AddWithValue("$start", minuteStart);
            insert.Parameters.AddWithValue("$counts", blob);
            await insert.ExecuteNonQueryAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repository = new KeyanaRepository(databasePath))
        {
            await repository.InitializeAsync();

            // 旧行还能读：计数一格不少，距离列取默认 0。
            var loaded = await repository.GetMinuteBucketAsync(minuteStart);
            Equal(4u, loaded!.Counts[(int)KeyId.A]);
            Equal(2u, loaded.Counts[(int)KeyId.Escape]);
            Equal(0L, loaded.WheelDistanceUp);
            Equal(0L, loaded.WheelDistanceDown);

            // 新列已存在且可写：同一分钟补写距离后，计数不被抹掉。
            await repository.UpsertMinuteBucketAsync(
                new MinuteBucketSnapshot(minuteStart, loaded.Counts, 600, 240));
            var updated = await repository.GetMinuteBucketAsync(minuteStart);
            Equal(600L, updated!.WheelDistanceUp);
            Equal(240L, updated.WheelDistanceDown);
            Equal(4u, updated.Counts[(int)KeyId.A]);

            var distance = await repository.QueryWheelDistanceAsync(minuteStart, minuteStart + 60);
            Equal(600L, distance.Up);
            Equal(240L, distance.Down);
            Equal(840L, distance.Total);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var verify = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await verify.OpenAsync();
        Equal("2", await ReadScalarAsync(verify, "PRAGMA user_version;"));
        Equal("2", await ReadScalarAsync(verify, "SELECT value FROM app_meta WHERE name = 'schema_version';"));
        Equal(
            "1",
            await ReadScalarAsync(
                verify,
                "SELECT COUNT(*) FROM pragma_table_info('minute_buckets') WHERE name = 'wheel_distance_up';"));
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static void WheelSettingsFallBackToDefaults()
{
    // 越界的窗口不是「钳到边界」，而是回默认值：钳过去会让人以为是自己设的。
    var tooSmall = new AppSettings { WheelGestureWindowMs = 10 };
    tooSmall.Validate();
    Equal(WheelGestureDetector.DefaultWindowMs, tooSmall.WheelGestureWindowMs);

    var tooLarge = new AppSettings { WheelGestureWindowMs = 900 };
    tooLarge.Validate();
    Equal(WheelGestureDetector.DefaultWindowMs, tooLarge.WheelGestureWindowMs);

    var valid = new AppSettings { WheelGestureWindowMs = 150 };
    valid.Validate();
    Equal(150, valid.WheelGestureWindowMs);

    var nanScale = new AppSettings { WheelDpiScale = double.NaN };
    nanScale.Validate();
    Equal(1.0, nanScale.WheelDpiScale);

    var zeroScale = new AppSettings { WheelDpiScale = 0 };
    zeroScale.Validate();
    Equal(1.0, zeroScale.WheelDpiScale);

    var validScale = new AppSettings { WheelDpiScale = 1.5 };
    validScale.Validate();
    Equal(1.5, validScale.WheelDpiScale);
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
              "fontScale": 9,
              "keyboardColorBasis": "Rainbow",
              "timelineBucketMinutes": 7,
              "mouseColorBasis": ""
            }
            """);

        var settings = await new SettingsStore(settingsPath).LoadOrCreateAsync();
        Equal("System", settings.Theme);
        Equal(1.0, settings.FontScale);
        Equal(string.Empty, settings.FontFamily);
        Equal(true, settings.SnapToTenMinutes);
        // 认不出的着色基准回到默认值，而不是让热力图整片变空。
        Equal("KeyboardOnly", settings.KeyboardColorBasis);
        Equal("KeyboardAndMouse", settings.MouseColorBasis);
        // 7 分钟不在阶梯上（不整除 60），回落到默认的 10 分钟一块。
        Equal(10, settings.TimelineBucketMinutes);
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

// ============ 构建信息 ============

static void ReleaseBuildShowsNumericVersion()
{
    var build = BuildInfo.FromRaw("1.3.4", "20261120-050240", "8986a94", "Clean", "Release");

    Equal("1.3.4", build.DisplayVersion);
    Equal("Release", build.BuildTypeText);
    Equal("Release", build.BuildType.ToString());
}

static void DevelopmentBuildAppendsDevSuffix()
{
    var build = BuildInfo.FromRaw("1.3.4", "20261120-050240", "8986a94", "Clean", "Debug");

    Equal("1.3.4-dev", build.DisplayVersion);
    Equal("Development", build.BuildTypeText);
    // 渠道标识与工作区状态无关：工作区干净也照样带 -dev。
    Equal("Clean", build.GitStatusText);
}

static void ReleaseBuildStripsDevelopmentSuffix()
{
    // csproj 里误写成 1.3.5-dev 时，正式包也不能带着渠道后缀发出去：
    // -dev 是构建配置派生的标记，不属于版本主体。
    var build = BuildInfo.FromRaw("1.3.5-dev", "20261120-153106", "9d8a9ff", "Clean", "Release");

    Equal("1.3.5", build.DisplayVersion);
}

static void DevelopmentBuildDoesNotDoubleDevSuffix()
{
    var build = BuildInfo.FromRaw("1.3.5-dev", "20261120-153106", "9d8a9ff", "Clean", "Debug");

    Equal("1.3.5-dev", build.DisplayVersion);
}

static void BuildIdIsShownOnlyInDevelopment()
{
    var development = BuildInfo.FromRaw("1.3.5", "20261120-153106", "9d8a9ff", "Clean", "Debug");
    var release = BuildInfo.FromRaw("1.3.5", "20261120-153106", "9d8a9ff", "Clean", "Release");

    // 侧边栏只在开发版多显示一行构建标识，正式版保持只有版本号。
    Equal("20261120-153106", development.DisplayBuildId);
    Equal<string?>(null, release.DisplayBuildId);
    Equal("1.3.5-dev", development.DisplayVersion);
    Equal("1.3.5", release.DisplayVersion);
}

static void DevSuffixIsIndependentOfGitStatus()
{
    var clean = BuildInfo.FromRaw("1.3.4", "20261120-050240", "8986a94", "Clean", "Development");
    var modified = BuildInfo.FromRaw("1.3.4", "20261120-050241", "8986a94", "Modified", "Development");

    Equal(clean.DisplayVersion, modified.DisplayVersion);
    Equal("1.3.4-dev", modified.DisplayVersion);
    Equal("Modified", modified.GitStatusText);
}

static void PrereleaseTagIsKept()
{
    // 版本号已经带预发布标识时原样保留，不重复追加 -dev，也不改成 -release。
    Equal("1.4.0-alpha.1", BuildInfo.FromRaw("1.4.0-alpha.1", "20261120-050240", "8986a94", "Clean", "Development").DisplayVersion);
    Equal("1.4.0-beta.2", BuildInfo.FromRaw("1.4.0-beta.2", "20261120-050240", "8986a94", "Clean", "Release").DisplayVersion);
}

static void MissingGitInfoIsDegraded()
{
    // 源码不在仓库里 / 构建机没有 Git：不抛异常，且不能把「拿不到」说成 Clean。
    var build = BuildInfo.FromRaw("1.3.4", "20261120-050240", "unknown", "Unknown", "Release");

    Equal(BuildInfo.UnknownValue, build.GitCommit);
    Equal(GitWorkingTreeState.Unknown, build.GitStatus);
    Equal("Unknown", build.GitStatusText);
    Equal("1.3.4", build.DisplayVersion);
}

static void NonHashGitOutputFallsBackToUnknown()
{
    // git 失败时会把 "fatal: not a git repository" 打到输出里，这种文本不能写进程序。
    var build = BuildInfo.FromRaw("1.3.4", "20261120-050240", "fatal: not a git repository", "Clean", "Release");

    Equal(BuildInfo.UnknownValue, build.GitCommit);
    Equal(GitWorkingTreeState.Unknown, build.GitStatus);
}

static void DiagnosticTextListsEveryField()
{
    var build = BuildInfo.FromRaw("1.3.4", "20261120-050240", "8986A94", "modified", "Release");
    var text = build.ToDiagnosticText();

    Contains("Version    : 1.3.4", text);
    Contains("Build ID   : 20261120-050240", text);
    Contains("Git Commit : 8986a94", text);
    Contains("Git Status : Modified", text);
    Contains("Build Type : Release", text);
}

static void KeyboardBasisIgnoresMouse()
{
    var counts = MakeMixedCounts();
    // 鼠标左键 800 次远高于任何键，键盘口径下不能被它拉低亮度。
    Equal(100UL, HeatBasis.Denominator(counts, ColorBasis.KeyboardOnly));
    Equal(1.0, Math.Round(HeatBasis.Intensity(counts[(int)KeyId.Digit1], 100UL), 4));
    Equal(0.5, Math.Round(HeatBasis.Intensity(counts[(int)KeyId.Digit2], 100UL), 4));
}

static void MouseBasisIgnoresKeyboard()
{
    var counts = MakeMixedCounts();
    Equal(800UL, HeatBasis.Denominator(counts, ColorBasis.MouseOnly));
}

static void CombinedBasisUsesGlobalMaximum()
{
    var counts = MakeMixedCounts();
    Equal(800UL, HeatBasis.Denominator(counts, ColorBasis.KeyboardAndMouse));
    // 键盘在合计口径下会被压浅，这正是该口径的语义，不是 bug。
    Equal(0.125, Math.Round(HeatBasis.Intensity(counts[(int)KeyId.Digit1], 800UL), 4));
    Equal(0UL, HeatBasis.Denominator(new ulong[KeyCatalog.Count], ColorBasis.KeyboardAndMouse));
    Equal(0.0, HeatBasis.Intensity(0, 0));
}

static void ColorBasisParseFallsBack()
{
    Equal(ColorBasis.MouseOnly, HeatBasis.Parse("mouseonly", ColorBasis.KeyboardOnly));
    Equal(ColorBasis.KeyboardOnly, HeatBasis.Parse("彩虹色", ColorBasis.KeyboardOnly));
    Equal(ColorBasis.KeyboardAndMouse, HeatBasis.Parse(null, ColorBasis.KeyboardAndMouse));
}

static void KeyFilterWithoutSelectionReusesCounts()
{
    var counts = MakeMixedCounts();
    Equal(true, ReferenceEquals(null, KeyFilter.Visible(counts, null)));
    // 空集合等于「什么都不统计」，没有意义，按未筛选处理。
    Equal(true, ReferenceEquals(null, KeyFilter.Visible(counts, new HashSet<KeyId>())));
    Equal("全部按键", KeyFilter.Describe(null));
}

static void KeyFilterKeepsOnlySelectedKeys()
{
    var counts = MakeMixedCounts();
    var visible = KeyFilter.Visible(counts, new HashSet<KeyId> { KeyId.Digit1, KeyId.MouseLeftButton })
                  ?? throw new InvalidOperationException("有选中键时必须返回副本。");

    Equal(100UL, visible[(int)KeyId.Digit1]);
    Equal(800UL, visible[(int)KeyId.MouseLeftButton]);
    Equal(0UL, visible[(int)KeyId.Digit2]);
    // 原数组不能被改动：取消筛选要能立刻恢复。
    Equal(50UL, counts[(int)KeyId.Digit2]);

    // 筛选后最亮键只在选中范围内取，键盘口径自然变成「选中键里的最大值」。
    Equal(100UL, HeatBasis.Denominator(visible, ColorBasis.KeyboardOnly));
    Equal(800UL, HeatBasis.Denominator(visible, ColorBasis.KeyboardAndMouse));
    Equal("已筛选 2 个键", KeyFilter.Describe(new HashSet<KeyId> { KeyId.Digit1, KeyId.MouseLeftButton }));
}

static void BuiltInLayoutsAreValid()
{
    // 重叠、越界、重复键都会在导入时才暴露，内置配列必须先在测试里过一遍。
    foreach (var layout in KeyLayouts.BuiltIn)
    {
        var errors = layout.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"{layout.Id}：" + string.Join("；", errors));
        }
    }

    Equal(true, KeyLayouts.BuiltIn.Count >= 6);
    Equal("ansi-104", KeyLayouts.Default.Id);
}

static void BuiltInLayoutKeyCountsMatchNames()
{
    // 104 / 87 / 84 / 68 / 60 是标准尺寸，键数必须对得上；
    // 「紧凑全尺寸」两套按实际键盘缺键命名，不在这里断言键数。
    Equal(104, KeyLayouts.Find("ansi-104")!.Caps.Count);
    Equal(87, KeyLayouts.Find("tkl-87")!.Caps.Count);
    Equal(84, KeyLayouts.Find("compact-84")!.Caps.Count);
    Equal(68, KeyLayouts.Find("compact-68")!.Caps.Count);
    Equal(61, KeyLayouts.Find("compact-60")!.Caps.Count);

    // 两套紧凑全尺寸只差 PrtSc 与 Ins 两颗键（用户描述的就是这两颗）。
    Equal(
        KeyLayouts.Find("compact-full-98")!.Caps.Count + 2,
        KeyLayouts.Find("compact-full-prtsc")!.Caps.Count);
}

static void BuiltInLayoutsExcludeMouseKeys()
{
    // 鼠标 7 键固定画在键盘下方一行，不参与配列，否则换配列会把它们弄丢。
    foreach (var layout in KeyLayouts.BuiltIn)
    {
        Equal(false, layout.Caps.Any(cap => HeatBasis.IsMouseKey(cap.Id)));
    }
}

static void LayoutJsonRoundTrips()
{
    var original = KeyLayouts.Default;
    var json = KeyLayoutJson.Serialize(original);

    Equal(true, KeyLayoutJson.TryParse(json, out var parsed, out var errors));
    Equal(string.Empty, string.Join("；", errors));
    Equal(original.Id, parsed!.Id);
    Equal(original.Caps.Count, parsed.Caps.Count);
    Equal(original.Columns, parsed.Columns);
    Equal(
        original.Caps.First(cap => cap.Id == KeyId.Space).Width,
        parsed.Caps.First(cap => cap.Id == KeyId.Space).Width);
}

static void OverlappingLayoutIsRejected()
{
    const string json = """
    {
      "version": 1,
      "id": "bad",
      "name": "重叠",
      "columns": 4,
      "keys": [
        { "key": "A", "x": 0, "y": 0 },
        { "key": "B", "x": 0.5, "y": 0 }
      ]
    }
    """;

    Equal(false, KeyLayoutJson.TryParse(json, out _, out var errors));
    Equal(true, errors.Any(error => error.Contains("重叠", StringComparison.Ordinal)));
}

static void UnknownLayoutKeyIsRejected()
{
    const string json = """
    {
      "version": 1,
      "id": "bad",
      "name": "未知键",
      "columns": 4,
      "keys": [ { "key": "HyperKey", "x": 0, "y": 0 } ]
    }
    """;

    Equal(false, KeyLayoutJson.TryParse(json, out _, out var errors));
    Equal(true, errors.Any(error => error.Contains("不是已知按键", StringComparison.Ordinal)));
}

static void UnsupportedLayoutVersionIsRejected()
{
    const string json = """
    {
      "version": 99,
      "id": "future",
      "name": "未来版本",
      "columns": 4,
      "keys": [ { "key": "A", "x": 0, "y": 0 } ]
    }
    """;

    Equal(false, KeyLayoutJson.TryParse(json, out _, out var errors));
    Equal(true, errors.Any(error => error.Contains("版本", StringComparison.Ordinal)));
}

static ulong[] MakeMixedCounts()
{
    var counts = new ulong[KeyCatalog.Count];
    counts[(int)KeyId.Digit1] = 100;
    counts[(int)KeyId.Digit2] = 50;
    counts[(int)KeyId.MouseLeftButton] = 800;
    return counts;
}

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

/// <summary>
/// 可注入失败、延迟与并发记录的内存存储，用来验证保存队列的重试/合并/退出语义。
/// </summary>
file sealed class FakeMinuteStore : IMinuteBucketStore
{
    private readonly object _gate = new();
    private readonly Dictionary<long, MinuteBucketSnapshot> _buckets = [];
    private readonly List<MinuteBucketSnapshot> _writes = [];
    private bool _alwaysFail;

    public TimeSpan WriteDelay { get; set; }

    public int FailuresRemaining { get; set; }

    public bool AlwaysFail
    {
        get
        {
            lock (_gate)
            {
                return _alwaysFail;
            }
        }
        set
        {
            lock (_gate)
            {
                _alwaysFail = value;
            }
        }
    }

    public Task<MinuteBucketSnapshot?> GetMinuteBucketAsync(
        long bucketStartUtc,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var snapshot = _buckets.TryGetValue(bucketStartUtc, out var stored) ? stored.Copy() : null;
            return Task.FromResult(snapshot);
        }
    }

    public async Task UpsertMinuteBucketAsync(
        MinuteBucketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (WriteDelay > TimeSpan.Zero)
        {
            await Task.Delay(WriteDelay, cancellationToken);
        }

        lock (_gate)
        {
            if (_alwaysFail || FailuresRemaining > 0)
            {
                if (!_alwaysFail)
                {
                    FailuresRemaining--;
                }

                throw new InvalidOperationException("模拟写入失败。");
            }

            _buckets[snapshot.BucketStartUtc] = snapshot.Copy();
            _writes.Add(snapshot.Copy());
        }
    }

    public long TotalFor(long bucketStartUtc)
    {
        lock (_gate)
        {
            return _buckets.TryGetValue(bucketStartUtc, out var snapshot) ? snapshot.TotalCount : 0;
        }
    }

    /// <summary>该分钟落盘的距离（上 / 下分开），与次数的 <see cref="TotalFor"/> 互不影响。</summary>
    public (long Up, long Down) DistancesFor(long bucketStartUtc)
    {
        lock (_gate)
        {
            return _buckets.TryGetValue(bucketStartUtc, out var snapshot)
                ? (snapshot.WheelDistanceUp, snapshot.WheelDistanceDown)
                : (0, 0);
        }
    }

    public int WriteCountFor(long bucketStartUtc)
    {
        lock (_gate)
        {
            return _writes.Count(write => write.BucketStartUtc == bucketStartUtc);
        }
    }

    public IReadOnlyList<long> WriteTotalsFor(long bucketStartUtc)
    {
        lock (_gate)
        {
            return _writes
                .Where(write => write.BucketStartUtc == bucketStartUtc)
                .Select(write => write.TotalCount)
                .ToArray();
        }
    }
}
