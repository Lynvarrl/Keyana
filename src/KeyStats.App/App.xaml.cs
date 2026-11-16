using System.Windows;
using System.Windows.Interop;

namespace KeyStats.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\KeyStats.SingleInstance.v1";
    private const string ShowSignalName = @"Local\KeyStats.Show.v1";
    private const string ExitSignalName = @"Local\KeyStats.Exit.v1";

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

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

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
