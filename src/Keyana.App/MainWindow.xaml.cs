using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Keyana.App.Controls;
using Keyana.App.Input;
using Keyana.App.Theme;
using Keyana.Core;
using Keyana.Storage;
using Microsoft.Win32;

namespace Keyana.App;

public partial class MainWindow : Window
{
    private readonly KeyboardCounter _counter = new();
    private readonly ObservableCollection<KeyCountRow> _rows;
    private readonly ICollectionView _rowsView;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _maintenanceTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly StartupRegistrationService _startupRegistration;
    private TrayIconService? _trayIcon;
    private RawInputKeyboardSource? _rawInput;
    private KeyanaRepository? _repository;
    private StatisticsRecorder? _recorder;
    private DateTimeOffset _lastStorageUiRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHeatmapUiRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAnalysisUiRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActivityUiRefresh = DateTimeOffset.MinValue;
    private bool _storageRefreshRunning;
    private bool _heatmapRefreshRunning;
    private bool _analysisRefreshRunning;
    private bool _isClosing;
    private bool _closeCompleted;
    private bool _uiReady;
    private bool _startupSettingChanging;
    private bool _paletteReady;
    private bool _settingsUiUpdating;
    private StatusMode _statusMode = StatusMode.Collecting;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;
    private TimeRangePreset _activeRangePreset = TimeRangePreset.Today;
    private (long StartUtc, long EndUtc)? _selectedRange;
    private (long StartUtc, long EndUtc)? _analysisRange;
    private (long StartUtc, long EndUtc)? _activityRange;
    // 用户在日历里选的原始本地起止。10 分钟吸附可能改写实际查询范围，但切换吸附
    // 设置时必须从原始值重算，不能拿已经吸附过的结果再吸一次。
    private (DateTime Start, DateTime End)? _customRangeInput;
    private (DateTime Start, DateTime End)? _customAnalysisInput;
    private (DateTime Start, DateTime End)? _customActivityInput;
    private TimeRangePreset _analysisPreset = TimeRangePreset.Recent30Days;
    private TimeRangePreset _activityPreset = TimeRangePreset.Recent30Days;
    private int _rangeActiveMinutes;
    private int _analysisActiveMinutes;
    private bool _durationUsesDays = true;
    private bool _syncingAnalysisPresetChips;
    private ulong[] _selectedCounts = new ulong[KeyCatalog.Count];
    private ulong[] _selectedStoredCounts = new ulong[KeyCatalog.Count];
    // 未经筛选的展示计数：筛选面板要用它显示每个键的真实次数。
    private ulong[] _selectedRawCounts = new ulong[KeyCatalog.Count];
    private ulong[] _analysisRawCounts = new ulong[KeyCatalog.Count];
    // 概览与分析各自独立筛选，只靠「同步到另一页」互相复制；null 表示未筛选。
    private IReadOnlySet<KeyId>? _overviewFilter;
    private IReadOnlySet<KeyId>? _analysisFilter;
    private ulong[] _selectedDisplayTimeCounts = new ulong[TenMinuteTimelineControl.SegmentCount];
    private ulong[] _selectedStoredTimeCounts = new ulong[TenMinuteTimelineControl.SegmentCount];
    private uint[]? _lastLiveCounts;
    private long? _selectedLiveBucketStart;
    private HashSet<KeyId> _lastVisualPressed = [];
    private HeatPalette _currentPalette = HeatPalette.BlueMagenta;
    private HeatScaleMode _currentScaleMode = HeatScaleMode.SquareRoot;
    private ColorBasis _keyboardBasis = ColorBasis.KeyboardOnly;
    private ColorBasis _mouseBasis = ColorBasis.KeyboardAndMouse;
    private bool _isElevated;
    private TaskCompletionSource<bool>? _confirmCompletion;

    public MainWindow()
    {
        InitializeComponent();

        // 版本信息全部来自构建时写入的 BuildInfo：侧边栏沿用原来的「v + 版本号」写法，
        // 只有渠道后缀由 DisplayVersion 决定（Release 不加、Development 带 -dev）；
        // 「关于」区域另外展示 Build ID / Git 等完整信息。
        var build = BuildInfoProvider.Current;
        Title = $"Keyana v{build.DisplayVersion} · 键盘与鼠标使用统计";
        VersionText.Text = $"v{build.DisplayVersion}";
        AboutVersionText.Text = $"Keyana v{build.DisplayVersion} · MIT License";
        ApplyBuildInfo(build);
        ApplyBuildStamp(build);

        _startupRegistration = new StartupRegistrationService(
            Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 Keyana 可执行文件路径。"));
        InitializeStartupSetting();
        _trayIcon = new TrayIconService(
            () => Dispatcher.BeginInvoke(ShowFromTray),
            () => Dispatcher.BeginInvoke(TogglePauseFromTray),
            () => Dispatcher.BeginInvoke(SaveFromTray),
            () => Dispatcher.BeginInvoke(ExitFromTray));

        _rows = new ObservableCollection<KeyCountRow>(
            KeyCatalog.Definitions.Select(definition => new KeyCountRow(definition)));
        _rowsView = CollectionViewSource.GetDefaultView(_rows);
        _rowsView.SortDescriptions.Add(new SortDescription(nameof(KeyCountRow.DisplayOrder), ListSortDirection.Ascending));
        _rowsView.Filter = FilterRow;
        KeyChipList.ItemsSource = _rowsView;

        PopulateFontFamilyChoices();
        PopulateColorBasisChoices();
        // 先按默认口径选中，别留两个空白下拉框：设置要等存储初始化完才读回来，
        // 这段时间里用户第一眼看到的就是它们。
        SelectColorBasis(KeyboardBasisComboBox, _keyboardBasis);
        SelectColorBasis(MouseBasisComboBox, _mouseBasis);
        InitializeLayouts();
        ApplyElevationState();
        UpdateDurationTexts();
        InitializeActivityDefaults();
        InitializeAnalysisDefaults();
        HeatmapKeyboard.SetData(
            _selectedCounts,
            new HashSet<KeyId>(),
            _currentScaleMode,
            _currentPalette,
            _keyboardBasis,
            _mouseBasis);
        DailyTimeline.SetData(_selectedDisplayTimeCounts, _currentPalette);
        ActivityStrip.SetPalette(_currentPalette);
        AnalysisDailyStrip.SetPalette(_currentPalette);
        UpdateGradientBar();

        // 两个筛选面板共用一个控件，但状态各存在自己页面上；同步按钮是唯一的跨页通道。
        OverviewFilterPanel.SyncTarget = "同步到分析页";
        AnalysisFilterPanel.SyncTarget = "同步到概览页";
        OverviewFilterPanel.FilterChanged += (_, _) => ApplyOverviewFilter();
        AnalysisFilterPanel.FilterChanged += (_, _) => ApplyAnalysisFilter();
        OverviewFilterPanel.SyncRequested += (_, _) => SyncFilter(overviewToAnalysis: true);
        AnalysisFilterPanel.SyncRequested += (_, _) => SyncFilter(overviewToAnalysis: false);

        InitializeWheelUi();

        _uiReady = true;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        // 隐藏到托盘后只跑这个低频定时器：界面刷新停掉，但 Alt+Tab 之类丢失
        // 松开消息的按键状态维护不能停，否则隐藏期间修饰键会一直「按着」。
        _maintenanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _maintenanceTimer.Tick += MaintenanceTimer_Tick;

        ThemeManager.ThemeChanged += OnThemeChanged;
        IsVisibleChanged += (_, _) => UpdateRefreshTimers();
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private enum StatusMode
    {
        Collecting,
        Paused,
        SaveError,
        Error,
    }

    private async void MainWindow_SourceInitialized(object? sender, EventArgs eventArgs)
    {
        ThemeManager.ApplyWindowChrome(this);

        // 先建存储再开采集。旧实现反着来：初始化失败的会话里采集照常跑、界面数字
        // 照常涨，但每一次 RecordKeyPress 都被 null 条件悄悄丢掉——用户看到的
        // 「正在采集」是假的。现在存储没就绪就不开放采集，并停在明确的错误态。
        await TryInitializeStorageAsync();

        if (_recorder is not null)
        {
            StartCollection();
        }
    }

    /// <summary>启动存储；失败时进入错误态、留下日志并显示「重试初始化」。</summary>
    private async Task TryInitializeStorageAsync()
    {
        RetryStorageButton.IsEnabled = false;
        try
        {
            await InitializeStorageAsync(_lifetimeCancellation.Token);
            RetryStorageButton.Visibility = Visibility.Collapsed;
            StatusBadge.ToolTip = null;
            _trayIcon?.SetError(null);
            _statusMode = _counter.IsPaused ? StatusMode.Paused : StatusMode.Collecting;
            RefreshStatusVisual();
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            if (_isClosing)
            {
                return;
            }

            ErrorLog.Record("Storage", exception);
            SetStorageError(exception.Message);
            SetErrorStatus();
            StatusBadge.ToolTip = $"本地数据不可用：{exception.Message}\n可在「设置」页点“重试初始化”。";
            _trayIcon?.SetError("本地数据不可用");
            RetryStorageButton.Visibility = Visibility.Visible;
        }
        finally
        {
            RetryStorageButton.IsEnabled = true;
        }
    }

    private void StartCollection()
    {
        try
        {
            if (_rawInput is null)
            {
                var windowHandle = new WindowInteropHelper(this).Handle;
                _rawInput = new RawInputKeyboardSource(
                    windowHandle,
                    _counter,
                    keyId => _recorder?.RecordKeyPress(keyId),
                    rawWheelSink: OnRawWheelObserved,
                    wheelDistanceSink: OnWheelDistanceObserved);
                _rawInput.WheelGestureWindowMs = _settings?.WheelGestureWindowMs
                    ?? WheelGestureDetector.DefaultWindowMs;
            }

            UpdateRefreshTimers();
        }
        catch (Exception exception)
        {
            ErrorLog.Record("RawInput", exception);
            SetErrorStatus();
            LastInputText.Text = $"采集启动失败：{exception.Message}";
            MessageBox.Show(
                this,
                $"无法启动键盘采集。\n\n{exception.Message}",
                "Keyana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RetryStorageButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        await TryInitializeStorageAsync();
        if (_recorder is not null)
        {
            StartCollection();
        }
    }

    private void RefreshTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _rawInput?.ReconcileSystemShortcutState();
        var snapshot = _counter.GetSnapshot();
        RunTotalText.Text = snapshot.TotalCount.ToString("N0");

        foreach (var row in _rows)
        {
            row.IsPressed = snapshot.PressedKeys.Contains(row.Id);
        }

        var selectedCountsChanged = RefreshLiveSelectedCounts();
        if (selectedCountsChanged || !_lastVisualPressed.SetEquals(snapshot.PressedKeys))
        {
            _lastVisualPressed = snapshot.PressedKeys.ToHashSet();
            RenderHeatmap(snapshot.PressedKeys);
        }

        PressedKeysText.Text = snapshot.PressedKeys.Count == 0
            ? "无"
            : string.Join("  +  ", snapshot.PressedKeys
                .OrderBy(key => (int)key)
                .Select(key => KeyCatalog.Get(key).DisplayName));

        if (_rawInput is not null)
        {
            LastInputText.Text = _rawInput.LastInputDescription;
            UnknownInputText.Text = _rawInput.LastUnrecognizedDescription is { } unknown
                ? $"未识别：{_rawInput.UnrecognizedMakeCount:N0} · 最近 {unknown}"
                : $"未识别：{_rawInput.UnrecognizedMakeCount:N0}";
        }

        if (ActiveOnlyCheckBox.IsChecked == true)
        {
            _rowsView.Refresh();
        }

        if (DateTimeOffset.UtcNow - _lastStorageUiRefresh >= TimeSpan.FromSeconds(2))
        {
            _ = RefreshStorageUiAsync();
        }
    }

    private void MaintenanceTimer_Tick(object? sender, EventArgs eventArgs) =>
        _rawInput?.ReconcileSystemShortcutState();

    /// <summary>
    /// 窗口可见时用 150 ms 的界面刷新；隐藏到托盘后只保留 1 s 的按键状态维护，
    /// 停掉渲染、查询与每 2 秒一次的总量 SUM，避免常驻时白白耗电。
    /// </summary>
    private void UpdateRefreshTimers()
    {
        if (_isClosing || _rawInput is null)
        {
            _refreshTimer.Stop();
            _maintenanceTimer.Stop();
            return;
        }

        if (IsVisible)
        {
            _maintenanceTimer.Stop();
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
            _maintenanceTimer.Start();
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs eventArgs) => SetPaused(!_counter.IsPaused);

    private void SetPaused(bool shouldPause)
    {
        _counter.SetPaused(shouldPause);
        // 暂停期间滚轮事件不进统计，但手势判定器仍在观察：不清掉时间戳，
        // 恢复后的第一下会被判成「上一次的续接」而丢一次动作。
        _rawInput?.ResetWheelState();
        _trayIcon?.SetPaused(shouldPause);
        PauseButton.Content = shouldPause ? "恢复采集" : "暂停采集";
        _statusMode = shouldPause ? StatusMode.Paused : StatusMode.Collecting;
        RefreshStatusVisual();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var result = MessageBox.Show(
            this,
            "只清空本次运行计数。已经保存的历史数据不会删除。\n\n确定继续吗？",
            "确认清零本次显示",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            Keyboard.ClearFocus();
            return;
        }

        _counter.ClearCounts(resetPressedState: true);
        _rawInput?.ClearDiagnostics();
        Keyboard.ClearFocus();
        RefreshTimer_Tick(sender, EventArgs.Empty);
    }

    private async void SaveNowButton_Click(object sender, RoutedEventArgs eventArgs) => await SaveNowAsync();

    private async Task SaveNowAsync()
    {
        if (_recorder is null)
        {
            return;
        }

        SaveNowButton.IsEnabled = false;
        try
        {
            await _recorder.FlushNowAsync(_lifetimeCancellation.Token);
            await RefreshStorageUiAsync(force: true);
        }
        catch (Exception exception) when (!_isClosing)
        {
            // 一次手动保存失败不等于数据不可用：计数仍在待写表里会自动重试。
            // 不打断用户，只把原因写进日志和状态行，徽章由 UpdateStatusFromRecorder 更新。
            ErrorLog.Record("Recorder.Flush", exception);
            StorageStatusText.Text = $"保存失败：{exception.Message}";
            UpdateStatusFromRecorder();
        }
        finally
        {
            if (!_isClosing)
            {
                SaveNowButton.IsEnabled = _recorder is not null;
            }
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_recorder is null || _repository is null || _selectedRange is not { } selectedRange)
        {
            return;
        }

        var rangeStartLocal = DateTimeOffset.FromUnixTimeSeconds(selectedRange.StartUtc).ToLocalTime();
        var rangeEndLocal = DateTimeOffset.FromUnixTimeSeconds(selectedRange.EndUtc).ToLocalTime();

        var dialog = new SaveFileDialog
        {
            Title = "导出所选时间范围的分钟聚合",
            Filter = "CSV 文件 (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".csv",
            FileName = $"keyana-{rangeStartLocal:yyyyMMdd-HHmm}-{rangeEndLocal:yyyyMMdd-HHmm}.csv",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            await _recorder.FlushNowAsync(_lifetimeCancellation.Token);
            await _repository.ExportCsvAsync(
                selectedRange.StartUtc,
                selectedRange.EndUtc,
                dialog.FileName,
                _lifetimeCancellation.Token);
            MessageBox.Show(this, "CSV 导出完成。", "Keyana", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (!_isClosing)
        {
            MessageBox.Show(this, $"导出失败：\n\n{exception.Message}", "Keyana", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!_isClosing)
            {
                ExportButton.IsEnabled = _repository is not null;
            }
        }
    }

    /// <summary>
    /// 数据库改名（key-stats.db → keyana.db）：交给 Storage 层先 checkpoint 再搬，
    /// 避免把未合并的 WAL 留在旧文件名下。迁移不安全时继续用旧文件，不因此启动失败。
    /// </summary>
    private static string ResolveDatabasePath(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "keyana.db");
        var legacyPath = Path.Combine(dataDirectory, "key-stats.db");
        if (!File.Exists(legacyPath) || File.Exists(databasePath))
        {
            return databasePath;
        }

        if (LegacyDatabaseMigrator.TryMigrate(legacyPath, databasePath, out var error))
        {
            return databasePath;
        }

        if (error is not null)
        {
            ErrorLog.Record(
                "Storage.Migration",
                new InvalidOperationException($"旧数据库迁移失败，本次继续使用 {legacyPath}：{error}"));
        }

        return legacyPath;
    }

    private async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        var settings = await settingsStore.LoadOrCreateAsync(cancellationToken);
        _settingsStore = settingsStore;
        _settings = settings;
        if (settingsStore.LastLoadWarning is { } warning)
        {
            // 设置坏了只降级外观，不阻断数据库初始化；但必须留下痕迹。
            ErrorLog.Record("Settings", new InvalidDataException(warning));
            StorageStatusText.ToolTip = warning;
        }

        ApplySettingsToUi(settings);

        var repository = new KeyanaRepository(ResolveDatabasePath(dataDirectory));
        StatisticsRecorder? recorder = null;
        try
        {
            await repository.InitializeAsync(cancellationToken);
            recorder = await StatisticsRecorder.CreateAsync(
                repository,
                TimeSpan.FromSeconds(settings.FlushIntervalSeconds),
                cancellationToken: cancellationToken);

            if (_isClosing)
            {
                await recorder.DisposeAsync();
                repository.Dispose();
                return;
            }

            _repository = repository;
            _recorder = recorder;
            SaveNowButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            await RefreshStorageUiAsync(force: true);
            // 启动瞬间如果已经点过预设，那时还没法读历史边界，这里按就绪后的数据重算一次。
            if (_analysisPreset != TimeRangePreset.Custom)
            {
                await SyncAnalysisRangeFromPresetAsync();
            }

            await RefreshAnalysisAsync(force: true);
        }
        catch
        {
            // 初始化后半段失败（刷新 UI、读区间）时 recorder 已经启动；不在这里收掉，
            // 重试会再建一个写入器，两个绝对快照互相覆盖。所有权在成功路径上转移给
            // 窗口字段，失败路径必须由这里释放。
            if (ReferenceEquals(_recorder, recorder))
            {
                _recorder = null;
                _repository = null;
            }

            if (recorder is not null)
            {
                await recorder.DisposeAsync();
            }

            repository.Dispose();
            throw;
        }
    }

    private async Task RefreshStorageUiAsync(bool force = false)
    {
        if (_storageRefreshRunning || _repository is null)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastStorageUiRefresh < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _storageRefreshRunning = true;
        try
        {
            var liveSnapshot = _recorder?.GetCurrentSnapshot();
            var storedTotal = await _repository.QueryTotalCountAsync(
                liveSnapshot?.BucketStartUtc,
                _lifetimeCancellation.Token);
            var totalCount = checked(storedTotal + (ulong)(liveSnapshot?.TotalCount ?? 0));
            TotalHistoryText.Text = $"总数量：{totalCount:N0}";

            var savedText = _recorder?.LastSavedUtc is { } savedUtc
                ? savedUtc.ToLocalTime().ToString("HH:mm:ss")
                : "尚未保存";
            var sizeMegabytes = _repository.GetStorageSizeBytes() / 1024d / 1024d;
            var errorText = _recorder?.LastError is { Length: > 0 } error
                ? $" · 最近错误：{error}"
                : string.Empty;

            DataPathText.Text = _repository.DatabasePath;
            DataSizeText.Text = $"数据库大小：{sizeMegabytes:F2} MB";
            StorageStatusText.Text = $"上次保存 {savedText}{errorText}";
            UpdateStatusFromRecorder();
            _lastStorageUiRefresh = DateTimeOffset.UtcNow;

            if (_activeRangePreset != TimeRangePreset.Custom)
            {
                _selectedRange = await ResolvePresetRangeAsync(_activeRangePreset);
                if (_uiReady && !OverviewRangePicker.IsEditorOpen)
                {
                    SyncRangePicker(_selectedRange.Value);
                }
            }

            await RefreshSelectedRangeAsync(force);

            if (AnalysisPage.Visibility == Visibility.Visible)
            {
                await RefreshAnalysisAsync(force);
            }
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                SetStorageError(exception.Message);
            }
        }
        finally
        {
            _storageRefreshRunning = false;
        }
    }

    private void SetCollectingStatus()
    {
        _statusMode = StatusMode.Collecting;
        RefreshStatusVisual();
    }

    private void SetErrorStatus()
    {
        _statusMode = StatusMode.Error;
        RefreshStatusVisual();
    }

    private void RefreshStatusVisual()
    {
        // 只换文字与圆点的颜色：绿/黄浅色底在卡片上太抢眼，用户要求去掉。
        // 校准中优先说出来：这时候滚轮并没有进统计，继续写「正在采集」是在骗人。
        var (text, foreground, dot) = _wheelCalibrating && _statusMode == StatusMode.Collecting
            ? ("滚轮校准中", "WarningBrush", "WarningBrush")
            : _statusMode switch
            {
                StatusMode.Paused => ("已暂停", "WarningBrush", "WarningBrush"),
                StatusMode.SaveError => ("保存异常", "DangerBrush", "DangerBrush"),
                StatusMode.Error => ("启动失败", "DangerBrush", "DangerBrush"),
                _ => ("正在采集", "SuccessBrush", "SuccessBrush"),
            };

        StatusText.Text = text;
        StatusText.Foreground = FindBrush(foreground);
        StatusBadge.Background = Brushes.Transparent;
        StatusDot.Fill = FindBrush(dot);
    }

    /// <summary>
    /// 保存失败时让状态徽章和托盘如实反映「采集中但没写进库」，而不是继续显示绿色
    /// 「正在采集」。写入恢复后自动回落（暂停状态优先保持）。
    /// </summary>
    private void UpdateStatusFromRecorder()
    {
        if (_isClosing || _statusMode == StatusMode.Error)
        {
            return;
        }

        if (_recorder?.LastError is { Length: > 0 })
        {
            _statusMode = StatusMode.SaveError;
            _trayIcon?.SetError("保存异常");
        }
        else
        {
            _statusMode = _counter.IsPaused ? StatusMode.Paused : StatusMode.Collecting;
            _trayIcon?.SetError(null);
        }

        RefreshStatusVisual();
    }

    private Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>
    /// 侧边栏只读展示当前进程的权限。
    ///
    /// 进程启动之后令牌不会再变，所以只在构造时取一次；用户判断「现在这个实例能不能
    /// 采到以管理员运行的游戏里的输入」，靠的就是这一行字。不做成按钮：切换权限必须
    /// 重启进程，入口统一放在设置页的重启浮层里。
    /// </summary>
    private void ApplyElevationState()
    {
        _isElevated = StartupRegistrationService.IsCurrentlyElevated();
        ElevationText.Text = _isElevated ? "· 管理员权限" : "· 普通权限";
        // 两种状态都不上色（不用黄/红），靠文字区分；但也不用最浅的灰，
        // 这一行的用途就是让人一眼确认，太淡等于没有。
        ElevationText.Foreground = FindBrush("SecondaryTextBrush");
        ElevationText.ToolTip = _isElevated
            ? "当前以管理员权限运行。以管理员权限运行的程序（如部分游戏）内的输入也会被统计。"
            : "当前为普通权限。以管理员权限运行的程序（如部分游戏）内的输入不会被统计。\n" +
              "可到设置页勾选「以管理员权限自启」，或在设置页用「重启」选择管理员权限重启。";
    }

    private void SetStorageError(string message)
    {
        TotalHistoryText.Text = "总数量：不可用";
        StorageStatusText.Text = $"本地数据错误：{message}";
        SaveNowButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
    }

    public void ShowFromTray()
    {
        if (_isClosing)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        // 隐藏期间停掉的界面刷新在这里补一次，避免刚打开时看到十几分钟前的数字。
        UpdateRefreshTimers();
        RefreshTimer_Tick(null, EventArgs.Empty);
        _ = RefreshStorageUiAsync(force: true);
    }

    private void TogglePauseFromTray() => SetPaused(!_counter.IsPaused);

    private async void SaveFromTray() => await SaveNowAsync();

    private async void ExitFromTray() => await ExitApplicationAsync(confirm: true);

    public async void ExitFromExternalRequest() => await ExitApplicationAsync(confirm: false);

    private async Task ExitApplicationAsync(bool confirm)
    {
        if (_isClosing || _closeCompleted)
        {
            return;
        }

        if (confirm)
        {
            var message = "完整退出后将停止键盘和鼠标采集。\n\n确定退出 Keyana 吗？";
            var result = IsVisible
                ? MessageBox.Show(this, message, "完整退出 Keyana", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(message, "完整退出 Keyana", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _isClosing = true;
        IsEnabled = false;
        // 托盘退出不会走 Closing（那里已经因为 _closeCompleted 提前返回），
        // 这里必须自己收掉校准浮层，否则退出前那一段滚轮仍是旁路状态。
        CloseWheelCalibration();
        _refreshTimer.Stop();
        _maintenanceTimer.Stop();
        _rawInput?.Dispose();
        _rawInput = null;
        _lifetimeCancellation.Cancel();

        try
        {
            if (_recorder is not null)
            {
                await _recorder.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            // 退出阶段无法安全恢复界面，但最后一次保存失败必须留下痕迹，
            // 否则用户只会觉得「今天少记了一些」。
            ErrorLog.Record("Recorder.Dispose", exception);
        }

        _recorder = null;
        _repository?.Dispose();
        _repository = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _closeCompleted = true;
        Application.Current.Shutdown();
    }

    private async void ExitButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_isClosing || _confirmCompletion is not null)
        {
            return;
        }

        var confirmed = await ShowConfirmAsync(
            "完整退出 Keyana",
            "退出后将停止键盘与鼠标采集。当前这一分钟的数据会先写入磁盘，已保存的历史数据不受影响。",
            "完整退出");
        if (!confirmed)
        {
            return;
        }

        await ExitApplicationAsync(confirm: false);
    }

    /// <summary>
    /// 页面内的二次确认浮层。
    ///
    /// 与托盘里的系统 MessageBox 并存：托盘触发时窗口可能是隐藏的，必须用系统框；
    /// 在主界面里则用这个浮层，才能跟随深色主题、并把默认焦点压在「取消」上。
    /// </summary>
    private Task<bool> ShowConfirmAsync(string title, string message, string acceptText)
    {
        ConfirmTitleText.Text = title;
        ConfirmMessageText.Text = message;
        ConfirmAcceptButton.Content = acceptText;
        ConfirmOverlay.Visibility = Visibility.Visible;

        _confirmCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfirmCancelButton.Focus();
        Keyboard.Focus(ConfirmCancelButton);
        return _confirmCompletion.Task;
    }

    private void CloseConfirm(bool accepted)
    {
        if (_confirmCompletion is not { } completion)
        {
            return;
        }

        _confirmCompletion = null;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        completion.TrySetResult(accepted);
    }

    private void ConfirmCancelButton_Click(object sender, RoutedEventArgs eventArgs) => CloseConfirm(false);

    private void ConfirmAcceptButton_Click(object sender, RoutedEventArgs eventArgs) => CloseConfirm(true);

    private void ConfirmOverlay_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            CloseConfirm(false);
            eventArgs.Handled = true;
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_isClosing)
        {
            return;
        }

        RestartPopupHint.Text = _isElevated
            ? "当前以管理员权限运行，重启后仍是管理员权限。"
            : "当前为普通权限。以管理员权限重启会弹出一次 UAC。";

        // Windows 没有「从高完整性进程降权启动」的公开接口：为了避免出现
        // 点了没反应、或者用计划任务绕一圈的复杂度，直接在管理员进程里禁用这一项。
        RestartNormalButton.IsEnabled = !_isElevated;
        RestartNormalButton.ToolTip = _isElevated
            ? "当前已是管理员权限，Windows 不支持直接降权重启。需要普通权限请先完整退出，再从开始菜单或任务栏重新打开。"
            : null;

        RestartPopup.IsOpen = true;
    }

    private async void RestartNormalButton_Click(object sender, RoutedEventArgs eventArgs) =>
        await RestartAsync(asAdministrator: false);

    private async void RestartElevatedButton_Click(object sender, RoutedEventArgs eventArgs) =>
        await RestartAsync(asAdministrator: true);

    /// <summary>
    /// 重启：先保存，再拉起「等旧进程退出」的新实例，最后走完整退出流程。
    ///
    /// 顺序不能反：新实例如果先跑起来，会发现单实例互斥量还被自己持有，误判成重复启动
    /// 直接退出，结果就是「点重启等于退出」。所以新实例带 --wait-for-exit，先等旧进程
    /// 结束再进入正常的单实例检查。提权重启时先弹 UAC，用户取消就什么都不做。
    /// </summary>
    private async Task RestartAsync(bool asAdministrator)
    {
        RestartPopup.IsOpen = false;
        if (_isClosing)
        {
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            MessageBox.Show(this, "无法确定 Keyana 可执行文件路径，重启失败。", "Keyana",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await SaveSettingsAsync();
        if (_recorder is not null)
        {
            try
            {
                await _recorder.FlushNowAsync(_lifetimeCancellation.Token);
            }
            catch (Exception exception) when (!_isClosing)
            {
                // 落盘失败不阻止重启：待写数据仍会在退出流程里再试一次。
                ErrorLog.Record("Restart.Flush", exception);
            }
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--wait-for-exit {Environment.ProcessId}",
            UseShellExecute = true,
        };
        if (asAdministrator && !_isElevated)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：用户在 UAC 上点了「否」。什么都不做，程序继续运行。
            return;
        }
        catch (Exception exception)
        {
            ErrorLog.Record("Restart", exception);
            MessageBox.Show(this, $"无法重新启动 Keyana。\n\n{exception.Message}", "Keyana",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await ExitApplicationAsync(confirm: false);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closeCompleted)
        {
            return;
        }

        // 收进托盘时把没处理完的确认浮层收掉：窗口隐藏后浮层还留在屏幕上会挡住别的程序。
        // 校准浮层同理，而且更要紧——它关掉才会把滚轮的旁路解除，否则会一直不记录。
        CloseConfirm(false);
        CloseWheelCalibration();
        eventArgs.Cancel = true;
        Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs eventArgs)
    {
        _refreshTimer.Stop();
        _maintenanceTimer.Stop();
        _wheelSettingsSaveTimer.Stop();
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _lifetimeCancellation.Dispose();
    }

    private void OnThemeChanged()
    {
        ThemeManager.ApplyWindowChrome(this);
        UpdateGradientBar();
        RefreshStatusVisual();
    }

    private void FilterChanged(object sender, RoutedEventArgs eventArgs) => _rowsView.Refresh();

    private bool FilterRow(object item)
    {
        if (ActiveOnlyCheckBox?.IsChecked != true)
        {
            return true;
        }

        return item is KeyCountRow row && (row.Count > 0 || row.IsPressed);
    }
}
