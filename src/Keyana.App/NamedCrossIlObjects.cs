using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Keyana.App;

/// <summary>
/// 创建“跨完整性级别（Cross-IL）可共享”的命名内核对象（Mutex / Event）。
///
/// 任务计划以最高权限启动的后台实例运行在高完整性级别（High IL）。默认情况下，
/// 之后以普通权限（Medium IL）启动的第二个实例打不开高 IL 进程创建的命名对象，
/// new Mutex/new EventWaitHandle 会直接抛异常。这里通过 Windows API 直接创建对象，
/// 并传入低完整性标签的安全描述符（SDDL），让任意完整性级别的同用户实例都能打开。
///
/// 不依赖 System.Threading.AccessControl 程序集，避免额外的包/引用问题。
/// </summary>
internal static class NamedCrossIlObjects
{
    private const int SddlRevision1 = 1;
    private const int ErrorAlreadyExists = 183;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl,
        int sddlRevision,
        out IntPtr securityDescriptor,
        IntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateMutexW(IntPtr securityAttributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(
        IntPtr securityAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string name);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    /// <summary>创建带低完整性标签安全描述符的命名 Mutex。</summary>
    public static Mutex CreateMutex(string name, string sddl, out bool createdNew)
    {
        var safeHandle = CreateWithSecurity(
            securityAttributes => CreateMutexW(securityAttributes, initialOwner: false, name),
            sddl,
            out createdNew);

        // 先构造未命名对象，再把句柄替换为自建的命名对象句柄。
        var mutex = new Mutex(initiallyOwned: false);
        mutex.SafeWaitHandle = safeHandle;
        return mutex;
    }

    /// <summary>创建带低完整性标签安全描述符的命名自动重置事件。</summary>
    public static EventWaitHandle CreateAutoResetEvent(string name, string sddl, out bool createdNew)
    {
        var safeHandle = CreateWithSecurity(
            securityAttributes => CreateEventW(securityAttributes, manualReset: false, initialState: false, name),
            sddl,
            out createdNew);

        var handle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);
        handle.SafeWaitHandle = safeHandle;
        return handle;
    }

    private static SafeWaitHandle CreateWithSecurity(Func<IntPtr, IntPtr> create, string sddl, out bool createdNew)
    {
        var descriptor = IntPtr.Zero;
        var attributesMemory = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    sddl, SddlRevision1, out descriptor, IntPtr.Zero))
            {
                throw new InvalidOperationException(
                    $"无法解析安全描述符，Win32 错误 {Marshal.GetLastWin32Error()}。");
            }

            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false,
            };
            attributesMemory = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityAttributes>());
            Marshal.StructureToPtr(attributes, attributesMemory, false);

            var rawHandle = create(attributesMemory);
            var lastError = Marshal.GetLastWin32Error();
            if (rawHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"无法创建命名内核对象，Win32 错误 {lastError}。");
            }

            // 命名对象已存在时，API 仍返回有效句柄，但 LastError 为 ERROR_ALREADY_EXISTS。
            createdNew = lastError != ErrorAlreadyExists;
            return new SafeWaitHandle(rawHandle, ownsHandle: true);
        }
        finally
        {
            if (attributesMemory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(attributesMemory);
            }

            if (descriptor != IntPtr.Zero)
            {
                LocalFree(descriptor);
            }
        }
    }
}
