using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Keyana.App;

/// <summary>
/// 管理 Keyana 的开机自启动。
///
/// 为什么不能用注册表 Run 键：当用户给 exe 勾选了“以管理员身份运行”兼容标志
/// （HKCU\...\AppCompatFlags\Layers 下的 ~ RUNASADMIN）时，登录阶段由未提权的
/// explorer 处理 HKCU\...\Run，无法在该流程中弹出 UAC，于是该启动项会被 Windows
/// 静默跳过，表现为“开机不自启、只能手动开”。
///
/// 方案：使用任务计划程序（Task Scheduler）创建“登录时触发、按当前用户身份运行”
/// （ONLOGON + InteractiveToken）的任务。任务由系统服务在用户登录后直接拉起，
/// 不经过 explorer 的 Run 键处理，也不依赖 UAC。
///
/// 权限模式（本次修正）：默认 <c>LeastPrivilege</c>，用户显式勾选“以管理员权限自启”
/// 时改用 <c>HighestAvailable</c>。旧实现无条件建最高权限任务，并把任务 XML 写到
/// %TEMP% 下一个固定名字、未提权进程可改的文件里，再让提权的 schtasks 读取——
/// 这是一条本地跨 UAC 边界的提权路径。
///
/// 现在两条路径都收紧了：
/// * 最小权限任务由当前进程直接创建，不需要 UAC，也不跨权限边界；
/// * 最高权限任务只有两种创建方式：当前进程已经提权，或通过 runas 启动本程序的
///   隐藏 helper（<c>--register-autostart=highest</c>）。XML 由提权后的进程自己
///   生成，写到 %ProgramData% 下随机命名的管理员专用目录，再交给同进程的 schtasks
///   导入，不再让提权进程读取普通用户可写的文件。
///
/// Keyana 默认不需要管理员权限；只有为了采集“以管理员运行的游戏/程序”的输入时，
/// 才值得勾选管理员自启。
///
/// 其他可靠性处理：
/// 1. 通过任务 XML 导入创建（而非 schtasks 命令行），显式关闭电池供电时禁止启动、
///    切电池即停止两项默认限制，把运行时限设为无限制（PT0S），并开启错过触发后的
///    补跑（StartWhenAvailable），解决笔记本上“偶发开机不自启”与长时间运行被终止。
/// 2. “是否已启用”通过解析任务的 XML（/Query /XML）判断，标签是稳定的英文，
///    不依赖中文系统的本地化字段名。它只看任务是否存在、是否指向当前 exe 并在
///    登录时触发；是否完全符合当前定义（权限模式、电池策略、运行时限、错过补跑）
///    单独返回，界面据此提示用户取消并重新勾选来重建，而不是把“还在自启”谎报成
///    “未启用”。
/// 3. 校验失败会回滚刚创建的任务，并把异常抛给界面，不留下“半配置”的启动项。
/// 4. 旧版 Run 键只在任务确认可用后才清理。
/// </summary>
internal sealed class StartupRegistrationService
{
    private const string TaskName = "Keyana AutoStart";
    private const string BackgroundArgument = "--background";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // 改名前的自启值名，只用于把老版本写下的 Run 键清理掉，不要跟着产品名一起改。
    private const string LegacyRunValueName = "KeyStats";

    private readonly string _executablePath;
    private readonly string _userId;
    private static readonly string SchtasksPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    static StartupRegistrationService()
    {
        // 让传统控制台程序（schtasks）的本地化错误文本可被正确解码。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// <paramref name="userId"/> 用于提权 helper：UAC 可能由另一个管理员账户授权，
    /// 但任务必须登记在发起请求的那个用户名下，所以要显式传 SID。
    /// </summary>
    public StartupRegistrationService(string executablePath, string? userId = null)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _userId = !string.IsNullOrWhiteSpace(userId)
            ? userId
            : WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    }

    /// <summary>
    /// 返回自启动任务的真实状态：是否存在并会在登录时拉起当前 exe、
    /// 当前是普通权限还是管理员权限、以及是否完全符合该权限模式的定义。
    /// </summary>
    public StartupRegistrationState GetRegistrationState()
    {
        // 只认任务计划。旧 Run 键在“以管理员身份运行”下本就不会触发，不能算已启用，
        // 否则界面会显示“已开”，用户永远不会重新勾选、也就无法迁移到任务计划。
        if (!TryQueryTask(out var task) || !task.StartsCurrentExecutable(_executablePath))
        {
            return StartupRegistrationState.NotRegistered;
        }

        var runAsAdministrator = task.RunAsAdministrator;
        return new StartupRegistrationState(
            IsRegistered: true,
            RunAsAdministrator: runAsAdministrator,
            MatchesCurrentDefinition: task.MatchesDefinition(_executablePath, runAsAdministrator));
    }

    public void SetEnabled(bool enabled, bool runAsAdministrator = false)
    {
        if (!enabled)
        {
            RemoveStartupTask();
            return;
        }

        if (runAsAdministrator && !IsProcessElevated())
        {
            // 当前是普通权限：通过 runas 启动本程序的隐藏 helper（一次 UAC），
            // 由提权后的进程自己生成并导入任务定义。
            RunElevatedRegistrationHelper();
            VerifyTaskOrThrow(runAsAdministrator: true);
            // 任务确认可用后再清理旧版 Run 键。
            RemoveLegacyRunKey();
            return;
        }

        CreateTaskLocally(runAsAdministrator);
    }

    /// <summary>
    /// 供提权 helper 调用：当前进程已经是管理员，直接把任务建出来。
    /// 返回进程退出码，0 表示成功。
    /// </summary>
    public static int RunRegistrationHelper(RegistrationHelperRequest request)
    {
        try
        {
            if (request.RunAsAdministrator && !IsProcessElevated())
            {
                // 没有真正提权就拒绝建最高权限任务，避免 helper 被普通权限重复调用。
                return 2;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return 3;
            }

            new StartupRegistrationService(executablePath, request.UserSid)
                .CreateTaskLocally(request.RunAsAdministrator);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// 解析 <c>--register-autostart=least|highest</c> 与可选的 <c>--user-sid=</c>。
    /// 返回 null 表示不是 helper 调用。
    /// </summary>
    public static RegistrationHelperRequest? ParseHelperArgument(IReadOnlyList<string> arguments)
    {
        const string modePrefix = "--register-autostart=";
        const string sidPrefix = "--user-sid=";

        bool? runAsAdministrator = null;
        string? userSid = null;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(modePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = argument[modePrefix.Length..].Trim();
                if (value.Equals("highest", StringComparison.OrdinalIgnoreCase))
                {
                    runAsAdministrator = true;
                }
                else if (value.Equals("least", StringComparison.OrdinalIgnoreCase))
                {
                    runAsAdministrator = false;
                }
            }
            else if (argument.StartsWith(sidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = argument[sidPrefix.Length..].Trim().Trim('"');
                // 只接受 SID 形态，避免把任意字符串塞进任务 XML 的 UserId。
                if (value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) && value.Length <= 184)
                {
                    userSid = value;
                }
            }
        }

        return runAsAdministrator is { } mode
            ? new RegistrationHelperRequest(mode, userSid)
            : null;
    }

    private void VerifyTaskOrThrow(bool runAsAdministrator)
    {
        if (!TryQueryTask(out var task) || !task.MatchesDefinition(_executablePath, runAsAdministrator))
        {
            throw new InvalidOperationException(runAsAdministrator
                ? "管理员自启任务创建后校验失败：任务缺失、运行级别不是最高权限，或电池/运行时限策略未生效。"
                : "自启动任务创建后校验失败：任务缺失、运行级别不是最小权限，或电池/运行时限策略未生效。");
        }
    }

    /// <summary>通过 UAC 启动本程序的 helper 来创建最高权限任务。</summary>
    private void RunElevatedRegistrationHelper()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = $"--register-autostart=highest --user-sid={_userId}",
                UseShellExecute = true, // runas 动词必须走 ShellExecute。
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动提权流程。");
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                throw new InvalidOperationException("等待管理员授权或创建自启任务超时。");
            }

            if (process.ExitCode != 0)
            {
                var reason = process.ExitCode switch
                {
                    2 => "提权后的进程没有管理员权限",
                    3 => "无法确定程序路径",
                    _ => "任务计划服务拒绝了创建请求，或任务校验未通过",
                };
                throw new InvalidOperationException(
                    $"管理员自启任务创建失败：{reason}（helper 退出码 {process.ExitCode}）。" +
                    "如果反复失败，可以先以管理员身份启动 Keyana，再重新勾选。");
            }
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // 1223 == ERROR_CANCELLED：用户在 UAC 对话框点了“否”。
            throw new InvalidOperationException("已取消管理员授权，因此没有启用管理员自启。");
        }
    }

    private void CreateTaskLocally(bool runAsAdministrator)
    {
        // 通过任务 XML 导入创建，而不是 schtasks /Create 命令行：
        // 命令行创建会套用 schtasks 默认设置——在笔记本上尤其致命：
        //   1) DisallowStartIfOnBatteries=true：电池供电时登录，任务被跳过，开机不自启；
        //   2) StopIfGoingOnBatteries=true：任务拉起的实例一旦切到电池就被终止；
        //   3) ExecutionTimeLimit 默认 PT72H：任务实例运行满 72 小时被强制终止；
        //   4) StartWhenAvailable=false：错过触发（登录瞬间任务服务未就绪）不补跑。
        // 因此显式写入 XML：关掉电池限制、运行时限设为无限制（PT0S）、开启错过补跑。
        // 注意编码：schtasks 导入任务 XML 只可靠接受 UTF-16（声明 UTF-16 且带 BOM）——
        // UTF-8 声明的文件会被 MSXML 以“无法切换编码”拒绝（实测）。因此 SaveTaskXml
        // 用 Encoding.Unicode 写出，与 schtasks /Query /XML 导出的原生格式完全一致。
        //
        // 写到哪里：普通权限创建用 %TEMP% 随机名，避免被预先占用同名路径；
        // 提权创建最高权限任务时，直接在 %ProgramData% 下新建随机目录 —— ProgramData
        // 只允许普通用户“新建”，不允许删除/改写别人的子项，而随机名字无法被预先创建，
        // 所以同会话的普通进程既进不去也换不掉里面的任务定义。
        var tempXmlPath = ResolveTaskXmlPath(
            runAsAdministrator && IsProcessElevated(),
            out var ownedDirectory);
        try
        {
            SaveTaskXml(BuildTaskXml(runAsAdministrator), tempXmlPath);

            RunSchtasksCaptured($"/Create /F /TN \"{TaskName}\" /XML \"{tempXmlPath}\"");

            // 结果回读校验：不依赖上一步的退出码文本，直接确认任务确实存在、路径与
            // 运行级别正确，并确认电池/时限/补跑策略生效，避免“命令看似成功但任务
            // 仍带默认限制（如电池不自启）”。
            if (!TryQueryTask(out var task) || !task.MatchesDefinition(_executablePath, runAsAdministrator))
            {
                TryDeleteTask();
                throw new InvalidOperationException(runAsAdministrator
                    ? "管理员自启任务创建后校验失败：任务缺失、运行级别不是最高权限、执行路径与当前程序不一致，或电池/运行时限策略未生效。已回滚本次创建。"
                    : "自启动任务创建后校验失败：任务缺失、运行级别不是最小权限、执行路径与当前程序不一致，或电池/运行时限策略未生效。已回滚本次创建。");
            }

            // 任务确认可用后再清理旧版 Run 键。
            RemoveLegacyRunKey();
        }
        finally
        {
            try
            {
                File.Delete(tempXmlPath);
            }
            catch
            {
                // 临时文件清理失败不影响任务本身。
            }

            if (ownedDirectory is not null)
            {
                try
                {
                    Directory.Delete(ownedDirectory, recursive: true);
                }
                catch
                {
                    // 空目录残留无害；下一次提权创建会用新的随机目录。
                }
            }
        }
    }

    private static string ResolveTaskXmlPath(bool adminOnly, out string? ownedDirectory)
    {
        ownedDirectory = null;
        if (!adminOnly)
        {
            return Path.Combine(Path.GetTempPath(), $"Keyana-{Path.GetRandomFileName()}.xml");
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            $"Keyana-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        ownedDirectory = directory;
        return Path.Combine(directory, "task.xml");
    }

    private static void TryDeleteTask()
    {
        try
        {
            RunSchtasksCaptured($"/Delete /F /TN \"{TaskName}\"");
        }
        catch
        {
            // 回滚失败不应掩盖原始错误；界面会显示校验失败的原因。
        }
    }

    /// <summary>
    /// 构造自启动任务的定义 XML。关键设置：登录触发 + 当前用户身份；
    /// 默认最小权限，<paramref name="runAsAdministrator"/> 为 true 时改为最高权限；
    /// 允许电池供电时启动、不因切到电池而停止；运行时长无限制；错过触发后补跑。
    /// </summary>
    private XDocument BuildTaskXml(bool runAsAdministrator)
    {
        var ns = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task");
        var sid = _userId;
        var startBoundary = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var runLevel = runAsAdministrator ? "HighestAvailable" : "LeastPrivilege";

        return new XDocument(
            new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "URI", "\\" + TaskName)),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", sid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", runLevel))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "StartBoundary", startBoundary))),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", $"\"{_executablePath}\""),
                        new XElement(ns + "Arguments", BackgroundArgument)))));
    }

    /// <summary>
    /// 以 schtasks 可接受的格式写出任务 XML：UTF-16（带 BOM），XML 声明随之写为
    /// encoding="utf-16"。schtasks /Query /XML 导出的正是这一格式；若写成 UTF-8，
    /// schtasks /Create /XML 会报 “The task XML is malformed … 无法切换编码”。
    /// </summary>
    private static void SaveTaskXml(XDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.Unicode, // UTF-16LE，带 BOM，声明 utf-16。
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };

        using (var writer = XmlWriter.Create(path, settings))
        {
            document.Save(writer);
        }
    }

    private void RemoveStartupTask()
    {
        // 任务本来就不存在时无需任何操作，直接清理旧 Run 键即可。
        // 任务以最小权限创建，删除也不需要提权。
        if (TryQueryTask(out _))
        {
            RunSchtasksCaptured($"/Delete /F /TN \"{TaskName}\"");
        }

        RemoveLegacyRunKey();
    }

    /// <summary>
    /// 以 /Query /XML 读取任务定义。XML 标签为稳定英文，避免依赖本地化输出。
    /// 注意：stdout 被重定向时，schtasks 实际输出的是系统代码页的单字节文本，却在
    /// XML 声明里写 encoding="UTF-16" 且不带 BOM；若按字节流交给 XDocument.Load，
    /// 会因“找不到 Unicode BOM、无法切换到 Unicode”而抛异常。因此这里按系统代码页
    /// 解码为字符串，再用 XDocument.Parse（解析字符串时不再做编码切换）。
    /// </summary>
    private static bool TryQueryTask(out TaskRegistrationInfo task)
    {
        task = default;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SchtasksPath,
                Arguments = $"/Query /TN \"{TaskName}\" /XML",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = TryGetConsoleEncoding(),
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            // 先并行排空两个流再等退出：旧实现先 ReadToEnd 再 WaitForExit，
            // 一旦子进程写满管道就会互相等待，5000 ms 的超时形同虚设。
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                return false;
            }

            var xmlText = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return false; // 任务不存在等情况，退出码非零。
            }

            var document = XDocument.Parse(xmlText);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            var exec = document.Descendants(ns + "Exec").FirstOrDefault();
            var command = (exec?.Element(ns + "Command")?.Value ?? string.Empty).Trim().Trim('"');
            var arguments = exec?.Element(ns + "Arguments")?.Value?.Trim() ?? string.Empty;
            var runLevel = document.Descendants(ns + "RunLevel").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
            var hasLogonTrigger = document.Descendants().Any(element => element.Name.LocalName == "LogonTrigger");

            // 以下策略元素在 schtasks /Create 命令行创建的旧任务里缺失（等于默认值）：
            // 缺失/为 true 都视为不符合可靠要求，从而触发用户重新勾选以 XML 方式重建。
            var disallowStartIfOnBatteries = IsElementTrue(document, ns, "DisallowStartIfOnBatteries");
            var stopIfGoingOnBatteries = IsElementTrue(document, ns, "StopIfGoingOnBatteries");
            var startWhenAvailable = IsElementTrue(document, ns, "StartWhenAvailable");
            var executionTimeLimit =
                document.Descendants(ns + "ExecutionTimeLimit").FirstOrDefault()?.Value?.Trim() ?? string.Empty;

            task = new TaskRegistrationInfo(
                command,
                arguments,
                runLevel,
                hasLogonTrigger,
                disallowStartIfOnBatteries,
                stopIfGoingOnBatteries,
                startWhenAvailable,
                executionTimeLimit);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsElementTrue(XDocument document, XNamespace ns, string localName)
    {
        var value = document.Descendants(ns + localName).FirstOrDefault()?.Value;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在当前权限下运行 schtasks，并捕获其错误输出；非零退出码时抛异常。</summary>
    private static void RunSchtasksCaptured(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = SchtasksPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = TryGetConsoleEncoding(),
            StandardOutputEncoding = TryGetConsoleEncoding(),
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");

        // 同 TryQueryTask：两个流并行读取，超时才真正生效，也不会因管道写满死锁。
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            throw new InvalidOperationException("schtasks 执行超时。");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            var detail = FirstNonBlank(stderr, stdout);
            throw new InvalidOperationException(
                $"schtasks 执行失败（退出码 {process.ExitCode}）：{detail}");
        }
    }

    /// <summary>
    /// 当前账户是否属于本机 Administrators 组。
    ///
    /// 不能用 <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/>：UAC 过滤后的
    /// 管理员令牌里该组是 deny-only，IsInRole 会返回 false，但组 SID 仍出现在
    /// <see cref="WindowsIdentity.Groups"/> 里。标准用户则完全不含这个 SID。
    /// </summary>
    public bool CanRunAsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity.Groups is null)
                {
                    return false;
                }

                var administrators = new SecurityIdentifier(
                    WellKnownSidType.BuiltinAdministratorsSid,
                    null);
                foreach (var group in identity.Groups)
                {
                    if (group.IsValidTargetType(typeof(SecurityIdentifier)) &&
                        ((SecurityIdentifier)group).Equals(administrators))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static Encoding TryGetConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(0); // 系统默认代码页（简体中文为 936）。
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string FirstNonBlank(params string[] texts)
    {
        foreach (var text in texts)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return "未知错误";
    }

    /// <summary>
    /// 清理旧版（v1.0）使用注册表 Run 键的自启动条目。
    /// </summary>
    private static void RemoveLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 清理旧条目失败不应阻断新方案。
        }
    }

    /// <summary>任务计划中与自启动相关的关键定义，用于回读校验。</summary>
    private readonly record struct TaskRegistrationInfo(
        string Command,
        string Arguments,
        string RunLevel,
        bool HasLogonTrigger,
        bool DisallowStartIfOnBatteries,
        bool StopIfGoingOnBatteries,
        bool StartWhenAvailable,
        string ExecutionTimeLimit)
    {
        /// <summary>是否以最高权限运行。</summary>
        public bool RunAsAdministrator =>
            RunLevel.Equals("HighestAvailable", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 任务会在登录时拉起当前 exe（带 --background）。只回答「装了没有」，
        /// 不判断权限与可靠性策略——这些交给 <see cref="MatchesDefinition"/>。
        /// </summary>
        public bool StartsCurrentExecutable(string executablePath)
        {
            if (!HasLogonTrigger || string.IsNullOrWhiteSpace(Command))
            {
                return false;
            }

            string normalizedCommand;
            try
            {
                normalizedCommand = Path.GetFullPath(Command);
            }
            catch
            {
                return false; // 畸形路径，视为不匹配。
            }

            if (!string.Equals(
                    normalizedCommand,
                    executablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var combined = $"{Command} {Arguments}";
            return combined.Contains(BackgroundArgument, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 任务是否完全符合给定模式的定义：RunLevel 与请求一致 + 关电池限制 +
        /// 无运行时限 + 错过补跑。
        /// </summary>
        public bool MatchesDefinition(string executablePath, bool runAsAdministrator)
        {
            if (!StartsCurrentExecutable(executablePath))
            {
                return false;
            }

            if (!RunLevel.Equals(
                    runAsAdministrator ? "HighestAvailable" : "LeastPrivilege",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 电池策略：必须允许电池供电时启动、且不因切到电池而停止（笔记本关键）。
            if (DisallowStartIfOnBatteries || StopIfGoingOnBatteries)
            {
                return false;
            }

            // 错过触发后必须补跑（登录瞬间任务计划服务可能尚未就绪）。
            if (!StartWhenAvailable)
            {
                return false;
            }

            // 运行时限必须无限制（PT0S），否则任务拉起的实例会在 72 小时后被系统终止。
            return ExecutionTimeLimit.Equals("PT0S", StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>提权 helper 的启动参数。</summary>
internal readonly record struct RegistrationHelperRequest(bool RunAsAdministrator, string? UserSid);

/// <summary>自启动任务的真实状态：是否存在、是否以管理员运行、是否符合当前定义。</summary>
internal readonly record struct StartupRegistrationState(
    bool IsRegistered,
    bool RunAsAdministrator,
    bool MatchesCurrentDefinition)
{
    public static StartupRegistrationState NotRegistered => new(false, false, false);
}
