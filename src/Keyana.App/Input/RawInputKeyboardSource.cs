using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Keyana.Core;

namespace Keyana.App.Input;

public sealed class RawInputKeyboardSource : IDisposable
{
    private const int WmInputDeviceChange = 0x00FE;
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;
    private const int GidcRemoval = 2;
    private const int VkMenu = 0x12;
    private const int VkTab = 0x09;
    private const ushort RiMouseLeftButtonDown = 0x0001;
    private const ushort RiMouseLeftButtonUp = 0x0002;
    private const ushort RiMouseRightButtonDown = 0x0004;
    private const ushort RiMouseRightButtonUp = 0x0008;
    private const ushort RiMouseMiddleButtonDown = 0x0010;
    private const ushort RiMouseMiddleButtonUp = 0x0020;
    private const ushort RiMouseButton4Down = 0x0040;
    private const ushort RiMouseButton4Up = 0x0080;
    private const ushort RiMouseButton5Down = 0x0100;
    private const ushort RiMouseButton5Up = 0x0200;
    private const ushort RiMouseWheel = 0x0400;

    private readonly KeyboardCounter _counter;
    private readonly Action<KeyId>? _countedKeySink;
    private readonly Action<WheelInputInfo>? _rawWheelSink;
    private readonly Action<WheelInputInfo>? _wheelDistanceSink;
    private readonly WheelGestureDetector _wheelDetector = new();
    private readonly HwndSource _windowSource;
    private readonly Timer _systemShortcutTimer;
    private readonly object _systemShortcutGate = new();
    private int _unrecognizedMakeCount;
    private int _wheelGestureWindowMs = WheelGestureDetector.DefaultWindowMs;
    private int _wheelBypass;
    private bool _sampledAltTabDown;
    private volatile bool _disposed;

    /// <param name="countedKeySink">已计数的一次输入（用于实时高亮）。</param>
    /// <param name="rawWheelSink">
    /// 每条真实滚轮事件的旁路观测。无论是否计数、是否处于校准模式都会触发，
    /// 校准面板靠它显示原始事件；不要在这里改统计状态。
    /// </param>
    /// <param name="wheelDistanceSink">
    /// 需要入库的滚动距离。暂停与校准旁路期间不触发。
    /// </param>
    public RawInputKeyboardSource(
        nint windowHandle,
        KeyboardCounter counter,
        Action<KeyId>? countedKeySink = null,
        Action<WheelInputInfo>? rawWheelSink = null,
        Action<WheelInputInfo>? wheelDistanceSink = null)
    {
        _counter = counter;
        _countedKeySink = countedKeySink;
        _rawWheelSink = rawWheelSink;
        _wheelDistanceSink = wheelDistanceSink;
        _windowSource = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法连接窗口消息源。");

        _windowSource.AddHook(WindowProcedure);

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x02,
                Flags = RidevInputSink | RidevDevNotify,
                TargetWindow = windowHandle,
            },
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevInputSink | RidevDevNotify,
                TargetWindow = windowHandle,
            },
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            _windowSource.RemoveHook(WindowProcedure);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册键盘和鼠标 Raw Input 失败。");
        }

        _systemShortcutTimer = new Timer(
            SampleSystemShortcutState,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public string LastInputDescription { get; private set; } = "等待键盘输入…";

    public int UnrecognizedMakeCount => Volatile.Read(ref _unrecognizedMakeCount);

    public string? LastUnrecognizedDescription { get; private set; }

    /// <summary>手势窗口（毫秒），热更新：只影响改动之后的新事件。</summary>
    public int WheelGestureWindowMs
    {
        get => Volatile.Read(ref _wheelGestureWindowMs);
        set => Volatile.Write(ref _wheelGestureWindowMs, WheelGestureDetector.ClampWindow(value));
    }

    /// <summary>
    /// 校准旁路：为 true 时滚轮事件仍然照常被观测与判定（面板要显示判定结果），
    /// 但不写入计数、不累加距离。其它键鼠完全不受影响。
    /// </summary>
    public bool WheelBypass
    {
        get => Volatile.Read(ref _wheelBypass) != 0;
        set => Volatile.Write(ref _wheelBypass, value ? 1 : 0);
    }

    /// <summary>清空手势判定状态。暂停恢复、校准开关切换、清空诊断时调用。</summary>
    public void ResetWheelState() => _wheelDetector.Reset();

    public void ReconcileSystemShortcutState()
    {
        ReleaseIfPhysicallyUp(KeyId.Tab, 0x09);
        ReleaseIfPhysicallyUp(KeyId.LeftAlt, 0xA4);
        ReleaseIfPhysicallyUp(KeyId.RightAlt, 0xA5);
    }

    private void SampleSystemShortcutState(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var altDown = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        var tabDown = (GetAsyncKeyState(VkTab) & 0x8000) != 0;
        lock (_systemShortcutGate)
        {
            if (_disposed)
            {
                return;
            }

            if (!altDown)
            {
                if (_sampledAltTabDown)
                {
                    _sampledAltTabDown = false;
                    _counter.ProcessButton(0, KeyId.Tab, isBreak: true);
                }

                _systemShortcutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            var altTabDown = altDown && tabDown;
            if (altTabDown == _sampledAltTabDown)
            {
                return;
            }

            _sampledAltTabDown = altTabDown;
            var result = _counter.ProcessButton(0, KeyId.Tab, isBreak: !altTabDown);
            if (result.Counted)
            {
                _countedKeySink?.Invoke(KeyId.Tab);
                LastInputDescription = "最近识别：Tab（Alt+Tab 系统切换补偿）";
            }

        }
    }

    private void UpdateSystemShortcutSampling(KeyId? keyId, bool isBreak)
    {
        if (keyId is not (KeyId.LeftAlt or KeyId.RightAlt))
        {
            return;
        }

        if (!isBreak)
        {
            _systemShortcutTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(15));
            return;
        }

        if ((GetAsyncKeyState(VkMenu) & 0x8000) != 0)
        {
            return;
        }

        _systemShortcutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_systemShortcutGate)
        {
            if (_sampledAltTabDown)
            {
                _sampledAltTabDown = false;
                _counter.ProcessButton(0, KeyId.Tab, isBreak: true);
            }
        }
    }

    public void ClearDiagnostics()
    {
        Interlocked.Exchange(ref _unrecognizedMakeCount, 0);
        LastInputDescription = "等待键盘输入…";
        LastUnrecognizedDescription = null;
        _wheelDetector.Reset();
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmInput)
        {
            ProcessRawInput(lParam);
        }
        else if (message == WmInputDeviceChange && wParam == GidcRemoval)
        {
            _counter.RemoveDevice(lParam);
            // 只清这台设备：另一台鼠标正在进行的滚动不该被打断。
            _wheelDetector.Reset(lParam);
        }

        // 保持 handled=false，让 WPF/DefWindowProc 完成 WM_INPUT 的系统清理。
        handled = false;
        return 0;
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        var rawInputSize = (uint)Marshal.SizeOf<RawInput>();
        var result = GetRawInputData(
            rawInputHandle,
            RidInput,
            out var rawInput,
            ref rawInputSize,
            (uint)Marshal.SizeOf<RawInputHeader>());

        if (result == uint.MaxValue)
        {
            return;
        }

        if (rawInput.Header.Type == RimTypeKeyboard)
        {
            ProcessKeyboard(rawInput);
        }
        else if (rawInput.Header.Type == RimTypeMouse)
        {
            ProcessMouse(rawInput);
        }
    }

    private void ProcessKeyboard(RawInput rawInput)
    {
        var keyboard = rawInput.Data.Keyboard;
        if (keyboard.MakeCode == 0xFF || keyboard.VirtualKey >= 0xFF)
        {
            // Windows 文档要求忽略键盘 overrun 状态和未映射到虚拟键的控制包。
            return;
        }

        var keyEvent = new RawKeyEvent(
            keyboard.MakeCode,
            (RawKeyFlags)keyboard.Flags,
            keyboard.VirtualKey);

        var processResult = _counter.ProcessDetailed(rawInput.Header.Device, keyEvent);
        UpdateSystemShortcutSampling(processResult.KeyId, keyEvent.IsBreak);
        if (processResult.Counted && processResult.KeyId is { } countedKey)
        {
            _countedKeySink?.Invoke(countedKey);
        }
        if (!keyEvent.IsBreak)
        {
            var normalized = KeyNormalizer.Normalize(keyEvent);
            LastInputDescription = normalized is { } keyId
                ? $"最近识别：{KeyCatalog.Get(keyId).DisplayName}  ·  Scan 0x{keyboard.MakeCode:X2}  ·  VK 0x{keyboard.VirtualKey:X2}"
                : $"最近未识别：Scan 0x{keyboard.MakeCode:X2}  ·  VK 0x{keyboard.VirtualKey:X2}  ·  Flags 0x{keyboard.Flags:X2}";

            if (!processResult.Recognized)
            {
                Interlocked.Increment(ref _unrecognizedMakeCount);
                LastUnrecognizedDescription = $"Scan 0x{keyboard.MakeCode:X2} · VK 0x{keyboard.VirtualKey:X2} · Flags 0x{keyboard.Flags:X2}";
            }
        }
    }

    private void ProcessMouse(RawInput rawInput)
    {
        var buttonFlags = rawInput.Data.Mouse.ButtonFlags;
        ProcessMouseButton(rawInput.Header.Device, buttonFlags, RiMouseLeftButtonDown, RiMouseLeftButtonUp, KeyId.MouseLeftButton);
        ProcessMouseButton(rawInput.Header.Device, buttonFlags, RiMouseRightButtonDown, RiMouseRightButtonUp, KeyId.MouseRightButton);
        ProcessMouseButton(rawInput.Header.Device, buttonFlags, RiMouseMiddleButtonDown, RiMouseMiddleButtonUp, KeyId.MouseMiddleButton);
        ProcessMouseButton(rawInput.Header.Device, buttonFlags, RiMouseButton4Down, RiMouseButton4Up, KeyId.MouseXButton1);
        ProcessMouseButton(rawInput.Header.Device, buttonFlags, RiMouseButton5Down, RiMouseButton5Up, KeyId.MouseXButton2);
        ProcessMouseWheel(rawInput.Header.Device, rawInput.Data.Mouse);
    }

    /// <summary>
    /// 滚轮没有按下 / 松开语义，一次动作会拆成很多条消息（高精度滚轮与触控板尤其碎）。
    ///
    /// 这里同时走两套互相独立的指标：
    /// <list type="bullet">
    /// <item><b>距离</b>——原始 delta 直接交给存储层按分钟累加，满 30 秒随桶落库。
    /// 不做 120 折算、不做封顶：上层想怎么解读都能从原始值算回来；</item>
    /// <item><b>手势动作数</b>——由 <see cref="WheelGestureDetector"/> 按静默窗口判定，
    /// 只有「距上次同向滚动超过窗口」才记 1 次。这一步不可逆，必须当场判定。</item>
    /// </list>
    ///
    /// 判定始终执行（校准面板要显示结果），旁路与暂停只挡住副作用。
    /// </summary>
    private void ProcessMouseWheel(nint deviceHandle, RawMouse mouse)
    {
        if ((mouse.ButtonFlags & RiMouseWheel) == 0)
        {
            return;
        }

        var delta = mouse.WheelDelta;
        var gesture = _wheelDetector.Observe(
            deviceHandle,
            delta,
            Environment.TickCount64,
            WheelGestureWindowMs);
        if (!gesture.Accepted)
        {
            return;
        }

        var isUp = gesture.Direction == WheelDirection.Up;
        var info = new WheelInputInfo(delta, gesture.Direction, gesture.IsNewGesture);

        // 旁路永远先发：校准面板看到的是真实事件，与是否入库无关。
        _rawWheelSink?.Invoke(info);

        if (!WheelBypass && !_counter.IsPaused)
        {
            _wheelDistanceSink?.Invoke(info);

            if (gesture.IsNewGesture)
            {
                var keyId = isUp ? KeyId.WheelGestureUp : KeyId.WheelGestureDown;
                if (_counter.RecordImpulse(keyId).Counted)
                {
                    _countedKeySink?.Invoke(keyId);
                }
            }
        }

        var directionName = isUp ? "滚轮上滑" : "滚轮下滑";
        var distance = $"距离 {(isUp ? "+" : "−")}{WheelMetrics.Magnitude(delta):N0}";
        LastInputDescription = gesture.IsNewGesture
            ? $"最近识别：{directionName}（手势） · {distance}"
            : $"最近识别：{directionName} · {distance}（并入本次手势）";
    }

    private void ProcessMouseButton(
        nint deviceHandle,
        ushort buttonFlags,
        ushort downFlag,
        ushort upFlag,
        KeyId keyId)
    {
        if ((buttonFlags & downFlag) != 0)
        {
            var result = _counter.ProcessButton(deviceHandle, keyId, isBreak: false);
            if (result.Counted)
            {
                _countedKeySink?.Invoke(keyId);
            }

            LastInputDescription = $"最近识别：{KeyCatalog.Get(keyId).DisplayName}";
        }

        if ((buttonFlags & upFlag) != 0)
        {
            _counter.ProcessButton(deviceHandle, keyId, isBreak: true);
        }
    }

    private void ReleaseIfPhysicallyUp(KeyId keyId, int virtualKey)
    {
        if ((GetAsyncKeyState(virtualKey) & 0x8000) == 0)
        {
            _counter.ReleaseKey(keyId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_systemShortcutGate)
        {
            _disposed = true;
            _systemShortcutTimer.Dispose();
        }
        _windowSource.RemoveHook(WindowProcedure);

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x02,
                Flags = RidevRemove,
                TargetWindow = 0,
            },
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevRemove,
                TargetWindow = 0,
            },
        };

        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] rawInputDevices,
        uint numberOfDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        out RawInput data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort Padding;
        public uint Buttons;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;

        public ushort ButtonFlags => (ushort)(Buttons & 0xFFFF);

        /// <summary>滚轮增量，位于 ulButtons 的高 16 位，按有符号 short 解释。</summary>
        public short WheelDelta => unchecked((short)(Buttons >> 16));
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawInputData
    {
        [FieldOffset(0)]
        public RawMouse Mouse;

        [FieldOffset(0)]
        public RawKeyboard Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawInputData Data;
    }
}
