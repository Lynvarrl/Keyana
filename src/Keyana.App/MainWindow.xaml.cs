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
    private TimeRangePreset _analysisPreset = TimeRangePreset.Recent30Days;
    private TimeRangePreset _activityPreset = TimeRangePreset.Recent30Days;
    private int _rangeActiveMinutes;
    private int _analysisActiveMinutes;
    private bool _durationUsesDays = true;
    private bool _syncingAnalysisPresetChips;
    private ulong[] _selectedCounts = new ulong[KeyCatalog.Count];
    private ulong[] _selectedStoredCounts = new ulong[KeyCatalog.Count];
    private ulong[] _selectedDisplayTimeCounts = new ulong[TenMinuteTimelineControl.SegmentCount];
    private ulong[] _selectedStoredTimeCounts = new ulong[TenMinuteTimelineControl.SegmentCount];
    private uint[]? _lastLiveCounts;
    private long? _selectedLiveBucketStart;
    private HashSet<KeyId> _lastVisualPressed = [];
    private HeatPalette _currentPalette = HeatPalette.BlueMagenta;
    private HeatScaleMode _currentScaleMode = HeatScaleMode.SquareRoot;

    public MainWindow()
    {
        InitializeComponent();

        Title = $"Keyana v{AppInfo.Version} · 键盘与鼠标使用统计";
        VersionText.Text = $"v{AppInfo.Version}";
        AboutVersionText.Text = $"Keyana v{AppInfo.Version} · MIT License";

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
        UpdateDurationTexts();
        InitializeActivityDefaults();
        InitializeAnalysisDefaults();
        HeatmapKeyboard.SetData(_selectedCounts, new HashSet<KeyId>(), _currentScaleMode, _currentPalette);
        DailyTimeline.SetData(_selectedDisplayTimeCounts, _currentPalette);
        ActivityStrip.SetPalette(_currentPalette);
        AnalysisDailyStrip.SetPalette(_currentPalette);
        UpdateGradientBar();

        _uiReady = true;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        ThemeManager.ThemeChanged += OnThemeChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private enum StatusMode
    {
        Collecting,
        Paused,
        Error,
    }

    private async void MainWindow_SourceInitialized(object? sender, EventArgs eventArgs)
    {
        ThemeManager.ApplyWindowChrome(this);

        try
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            _rawInput = new RawInputKeyboardSource(
                windowHandle,
                _counter,
                keyId => _recorder?.RecordKeyPress(keyId));
            _refreshTimer.Start();
        }
        catch (Exception exception)
        {
            SetErrorStatus();
            LastInputText.Text = $"采集启动失败：{exception.Message}";
            MessageBox.Show(
                this,
                $"无法启动键盘采集。\n\n{exception.Message}",
                "Keyana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            await InitializeStorageAsync(_lifetimeCancellation.Token);
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

    private void PauseButton_Click(object sender, RoutedEventArgs eventArgs) => SetPaused(!_counter.IsPaused);

    private void SetPaused(bool shouldPause)
    {
        _counter.SetPaused(shouldPause);
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
            SetStorageError(exception.Message);
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
    /// 数据库改名（key-stats.db → keyana.db）：新文件名还不存在、旧文件存在时直接改名，
    /// 免得升级后看起来像「历史数据全丢了」。改名失败就继续用旧文件，不因此启动失败。
    /// </summary>
    private static string PrepareDatabasePath(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "keyana.db");
        var legacyPath = Path.Combine(dataDirectory, "key-stats.db");
        if (File.Exists(databasePath) || !File.Exists(legacyPath))
        {
            return databasePath;
        }

        try
        {
            File.Move(legacyPath, databasePath);
            // WAL 模式下 db 旁边还有 -wal / -shm，一起改名才算完整迁移。
            MoveIfExists(legacyPath + "-wal", databasePath + "-wal");
            MoveIfExists(legacyPath + "-shm", databasePath + "-shm");
            return databasePath;
        }
        catch (IOException)
        {
            return legacyPath;
        }
        catch (UnauthorizedAccessException)
        {
            return legacyPath;
        }
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        try
        {
            File.Move(source, destination, overwrite: true);
        }
        catch (IOException)
        {
            // 旁挂文件搬不动不影响主库，SQLite 会按需重建。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        var settings = await settingsStore.LoadOrCreateAsync(cancellationToken);
        _settingsStore = settingsStore;
        _settings = settings;
        ApplySettingsToUi(settings);

        var repository = new KeyanaRepository(PrepareDatabasePath(dataDirectory));
        try
        {
            await repository.InitializeAsync(cancellationToken);
            var recorder = await StatisticsRecorder.CreateAsync(
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
        var (text, foreground, dot) = _statusMode switch
        {
            StatusMode.Paused => ("已暂停", "WarningBrush", "WarningBrush"),
            StatusMode.Error => ("启动失败", "DangerBrush", "DangerBrush"),
            _ => ("正在采集", "SuccessBrush", "SuccessBrush"),
        };

        StatusText.Text = text;
        StatusText.Foreground = FindBrush(foreground);
        StatusBadge.Background = Brushes.Transparent;
        StatusDot.Fill = FindBrush(dot);
    }

    private Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

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
        _refreshTimer.Stop();
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
        catch
        {
            // 退出阶段无法安全恢复界面；已经提交的 SQLite 事务仍保持一致。
        }

        _recorder = null;
        _repository?.Dispose();
        _repository = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _closeCompleted = true;
        Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closeCompleted)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        _trayIcon?.ShowHiddenNotification();
    }

    private void MainWindow_Closed(object? sender, EventArgs eventArgs)
    {
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
