using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Keyana.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\Keyana.SingleInstance.v1";
    private const string ShowSignalName = @"Local\Keyana.Show.v1";
    private const string ExitSignalName = @"Local\Keyana.Exit.v1";

    // 允许 Everyone 访问，并把命名内核对象的强制完整性标签设为 Low。
    // 任务计划以最高权限启动的后台实例运行在高完整性级别（High IL）；若之后用户以
    // 普通权限（Medium IL）再次启动，默认情况下后者打不开高 IL 进程创建的 Mutex/Event，
    // 会直接抛异常闪退、也无法唤起已有窗口。低完整性标签让任意 IL 的实例都能共享。
    private const string CrossIntegritySddl = "D:(A;;GA;;;WD)S:(ML;;NW;;;LW)";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _showWait;
    private RegisteredWaitHandle? _exitWait;
    private MainWindow? _mainWindow;
    private bool _unhandledErrorNotified;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        // 隐藏 helper：被 runas 提权后用来创建/删除自启任务。必须放在单实例检查
        // 与窗口创建之前——主实例通常已经在跑，helper 不能被当成第二个实例对待。
        if (StartupRegistrationService.IsUnregistrationHelperArgument(eventArgs.Args))
        {
            Shutdown(StartupRegistrationService.RunUnregistrationHelper());
            return;
        }

        if (StartupRegistrationService.ParseHelperArgument(eventArgs.Args) is { } helperRequest)
        {
            Shutdown(StartupRegistrationService.RunRegistrationHelper(helperRequest));
            return;
        }

        RegisterGlobalExceptionHandling();
        // 必须在创建窗口前装好主题，否则第一帧会按浅色画完再跳成深色。
        Theme.ThemeManager.Initialize();

        var startInBackground = eventArgs.Args.Any(
            argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var requestExit = eventArgs.Args.Any(
            argument => string.Equals(argument, "--exit", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = CreateSingleInstanceMutex(SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance(requestExit ? ExitSignalName : ShowSignalName, startInBackground && !requestExit);
            Shutdown();
            return;
        }

        if (requestExit)
        {
            Shutdown();
            return;
        }

        _showSignal = CreateLocalEvent(ShowSignalName);
        _exitSignal = CreateLocalEvent(ExitSignalName);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.BeginInvoke(_mainWindow.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitSignal,
            (_, _) => Dispatcher.BeginInvoke(_mainWindow.ExitFromExternalRequest),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        if (startInBackground)
        {
            _ = new WindowInteropHelper(_mainWindow).EnsureHandle();
        }
        else
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _showWait?.Unregister(null);
        _exitWait?.Unregister(null);
        _showSignal?.Dispose();
        _exitSignal?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(eventArgs);
    }

    /// <summary>
    /// 注册三层未处理异常兜底。托盘菜单和进程间信号回调里有若干 async void 方法，
    /// 它们内部抛出的异常不会传到任何调用者；没有兜底时进程会直接消失，而这个程序
    /// 平时藏在托盘里，用户只会看到统计停止增长。
    /// </summary>
    private void RegisterGlobalExceptionHandling()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        ErrorLog.Record("Dispatcher", eventArgs.Exception);

        // 保持运行：统计程序挂在一次瞬时 UI 异常上毫无价值，数据都在 SQLite 里，
        // 继续采集比直接退出更符合用户预期。重复异常只提示一次，避免弹窗刷屏。
        eventArgs.Handled = true;
        if (_unhandledErrorNotified)
        {
            return;
        }

        _unhandledErrorNotified = true;
        try
        {
            MessageBox.Show(
                $"Keyana 捕获到一个未处理的错误，已记录到日志，程序会继续运行。\n\n" +
                $"日志：{ErrorLog.LogPath}\n\n{eventArgs.Exception.Message}",
                "Keyana",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // 连提示框都弹不出来时，日志已经落盘，不再做任何事。
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        ErrorLog.Record("TaskScheduler", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        // 走到这里进程已经在终止路径上，只能记录，无法恢复。
        if (eventArgs.ExceptionObject is Exception exception)
        {
            ErrorLog.Record("AppDomain", exception);
        }
    }

    /// <summary>
    /// 创建跨完整性级别可共享的单实例 Mutex；低标签方式不可用时退回普通命名 Mutex。
    /// </summary>
    private static Mutex CreateSingleInstanceMutex(string name, out bool createdNew)
    {
        try
        {
            return NamedCrossIlObjects.CreateMutex(name, CrossIntegritySddl, out createdNew);
        }
        catch
        {
            return new Mutex(initiallyOwned: false, name, out createdNew);
        }
    }

    /// <summary>
    /// 创建跨完整性级别可共享的本地自动重置事件；失败时退回普通命名事件。
    /// </summary>
    private static EventWaitHandle CreateLocalEvent(string name)
    {
        try
        {
            return NamedCrossIlObjects.CreateAutoResetEvent(name, CrossIntegritySddl, out _);
        }
        catch
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
    }

    private static void SignalExistingInstance(string signalName, bool suppressSignal)
    {
        if (suppressSignal)
        {
            return;
        }

        try
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
            signal.Set();
        }
        catch
        {
            // 无法联系已运行实例时不应让第二个实例崩溃；直接退出即可。
        }
    }
}
