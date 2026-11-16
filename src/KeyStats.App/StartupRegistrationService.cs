using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace KeyStats.App;

/// <summary>
/// 管理 KeyStats 的开机自启动。
///
/// 为什么不能用注册表 Run 键：当用户给 exe 勾选了“以管理员身份运行”兼容标志
/// （HKCU\...\AppCompatFlags\Layers 下的 ~ RUNASADMIN）时，登录阶段由未提权的
/// explorer 处理 HKCU\...\Run，无法在该流程中弹出 UAC，于是该启动项会被 Windows
/// 静默跳过，表现为“开机不自启、只能手动开”。
///
/// 可靠方案：使用任务计划程序（Task Scheduler）创建“登录时触发、以最高权限运行”
/// （ONLOGON + /RL HIGHEST）的任务。任务由系统服务用提升后的令牌直接拉起，无需
/// UAC 弹窗，因此同时兼容“普通权限”和“以管理员身份运行”两种用法。
///
/// 本类的可靠性处理：
/// 1. 创建“最高权限”任务本身需要管理员权限；当前进程未提权时，自动通过 runas
///    动词对 schtasks.exe 做一次提权（用户会看到一次 UAC）。
/// 2. 不再使用 /DELAY，避免登录后的启动空窗以及与用户手动启动的实例竞争。
/// 3. 通过任务 XML 导入创建（而非 schtasks 命令行），显式关闭电池供电时禁止启动、
///    切电池即停止两项默认限制，把运行时限设为无限制（PT0S），并开启错过触发后的
///    补跑（StartWhenAvailable），解决笔记本上“偶发开机不自启”与长时间运行被终止。
/// 4. “是否已启用”通过解析任务的 XML（/Query /XML）判断，标签是稳定的英文，
///    不依赖中文系统的本地化字段名；同时校验 RunLevel、登录触发器、可执行路径、
///    电池策略、运行时限与补跑设置。
/// 5. 旧版 Run 键只在任务确认创建成功后才清理；任务创建失败时保留旧条目并抛错。
///    旧 Run 键不再被视为“已启用”，避免在 RUNASADMIN 下显示已开却实际不自启。
/// </summary>
internal sealed class StartupRegistrationService
{
    private const string TaskName = "KeyStats AutoStart";
    private const string BackgroundArgument = "--background";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValueName = "KeyStats";

    private readonly string _executablePath;
    private static readonly string SchtasksPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    static StartupRegistrationService()
    {
        // 让传统控制台程序（schtasks）的本地化错误文本可被正确解码。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public StartupRegistrationService(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsEnabledForCurrentExecutable()
    {
        // 只认任务计划。旧 Run 键在“以管理员身份运行”下本就不会触发，不能算已启用，
        // 否则界面会显示“已开”，用户永远不会重新勾选、也就无法迁移到任务计划。
        return TryQueryTask(out var task) && task.Matches(_executablePath);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateStartupTask();
        }
        else
        {
            RemoveStartupTask();
        }
    }

    private void CreateStartupTask()
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
        var tempXmlPath = Path.Combine(Path.GetTempPath(), "KeyStats-AutoStart-task.xml");
        try
        {
            SaveTaskXml(BuildTaskXml(), tempXmlPath);

            var arguments = $"/Create /F /TN \"{TaskName}\" /XML \"{tempXmlPath}\"";
            if (IsProcessElevated())
            {
                RunSchtasksCaptured(arguments);
            }
            else
            {
                // 创建最高权限任务需要管理员：提权运行一次 schtasks（一次 UAC）。
                RunSchtasksElevated(arguments);
            }

            // 结果回读校验：不依赖上一步的退出码文本，直接确认任务确实存在、路径与
            // 运行级别正确，并确认电池/时限/补跑策略生效，避免“命令看似成功但任务
            // 仍带默认限制（如电池不自启）”。
            if (!TryQueryTask(out var task) || !task.Matches(_executablePath))
            {
                throw new InvalidOperationException(
                    "自启动任务创建后校验失败：任务缺失、运行级别不是最高权限、执行路径与当前程序不一致，或电池/运行时限策略未生效。");
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
        }
    }

    /// <summary>
    /// 构造自启动任务的定义 XML。关键设置：登录触发 + 最高权限运行；
    /// 允许电池供电时启动、不因切到电池而停止；运行时长无限制；错过触发后补跑。
    /// </summary>
    private XDocument BuildTaskXml()
    {
        var ns = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task");
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var startBoundary = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

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
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
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
        // 任务本来就不存在时无需任何提权，直接清理旧 Run 键即可。
        if (TryQueryTask(out _))
        {
            try
            {
                RunSchtasksCaptured($"/Delete /F /TN \"{TaskName}\"");
            }
            catch (InvalidOperationException)
            {
                // 删除自己的任务通常不需要提权；个别策略下被拒时再提权重试一次。
                RunSchtasksElevated($"/Delete /F /TN \"{TaskName}\"");
            }
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

            var xmlText = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
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
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(); } catch { /* 忽略 */ }
            throw new InvalidOperationException("schtasks 执行超时。");
        }

        if (process.ExitCode != 0)
        {
            var detail = FirstNonBlank(stderr, stdout);
            throw new InvalidOperationException(
                $"schtasks 执行失败（退出码 {process.ExitCode}）：{detail}");
        }
    }

    /// <summary>通过 UAC 提权运行 schtasks（用于当前进程未提权的情况）。</summary>
    private static void RunSchtasksElevated(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SchtasksPath,
                Arguments = arguments,
                UseShellExecute = true, // runas 动词必须使用 ShellExecute。
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动提权流程。");
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(); } catch { /* 忽略 */ }
                throw new InvalidOperationException("提权创建自启动任务超时。");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"提权执行 schtasks 失败（退出码 {process.ExitCode}）。");
            }
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // 1223 == ERROR_CANCELLED：用户在 UAC 对话框点了“否”。
            throw new InvalidOperationException("已取消管理员授权，因此没有启用开机自启动。");
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
        public bool Matches(string executablePath)
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
            if (!combined.Contains(BackgroundArgument, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!RunLevel.Equals("HighestAvailable", StringComparison.OrdinalIgnoreCase))
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
