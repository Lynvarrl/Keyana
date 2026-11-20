namespace Keyana.Core;

/// <summary>构建渠道：Debug 配置的构建为 Development，Release 配置的构建为 Release。</summary>
public enum BuildChannel
{
    Development,
    Release,
}

/// <summary>构建那一刻 Git 工作区的状态。</summary>
public enum GitWorkingTreeState
{
    /// <summary>没有未提交的修改。</summary>
    Clean,

    /// <summary>存在未提交的修改（包含未跟踪文件）。</summary>
    Modified,

    /// <summary>构建环境拿不到 Git 信息，无法判断。</summary>
    Unknown,
}

/// <summary>
/// 一次构建的身份信息：版本号、构建标识、Git 提交、工作区状态与构建渠道。
///
/// 这个类型只负责「承载 + 展示」这些值，取值时机在构建阶段（由 scripts/build-info.ps1
/// 写进程序集），因此同一份产物无论启动多少次，Build ID 都不会变化；打包后的 EXE
/// 也不需要 Git、不需要旁边的任何文件就能报出自己是哪次构建。
///
/// 所有字段都做了降级处理：缺少 Git、版本号为空等异常输入都会落到明确的可读值上，
/// 不会抛异常，也不会把「拿不到」误报成「干净」。
/// </summary>
public sealed record BuildInfo
{
    /// <summary>无法取得某项信息时统一使用的占位值。</summary>
    public const string UnknownValue = "unknown";

    public BuildInfo(
        string? version,
        string? buildId,
        string? gitCommit,
        GitWorkingTreeState gitStatus,
        BuildChannel buildType)
    {
        Version = NormalizeVersion(version);
        BuildId = NormalizeBuildId(buildId);
        GitCommit = NormalizeCommit(gitCommit);
        // 拿不到提交哈希时工作区状态也无从判断，统一标成 Unknown：
        // 在没有 Git 的打包环境里显示 Clean 会误导排查方向。
        GitStatus = GitCommit == UnknownValue ? GitWorkingTreeState.Unknown : gitStatus;
        BuildType = buildType;
    }

    /// <summary>软件版本号，来自 Keyana.App.csproj 的 &lt;Version&gt;。</summary>
    public string Version { get; }

    /// <summary>构建标识，格式 <c>YYYYMMDD-HHMMSS</c>，在构建时生成一次。</summary>
    public string BuildId { get; }

    /// <summary>构建时 Git HEAD 的短哈希；不可用时为 <see cref="UnknownValue"/>。</summary>
    public string GitCommit { get; }

    /// <summary>构建时 Git 工作区是否有未提交修改。</summary>
    public GitWorkingTreeState GitStatus { get; }

    /// <summary>Development 或 Release。</summary>
    public BuildChannel BuildType { get; }

    /// <summary>
    /// 侧边栏与「关于」共用的版本标签：Release 只给数字版本号，Development 追加 <c>-dev</c>。
    /// 版本号本身已经带预发布标识（如 <c>1.4.0-alpha.1</c>）时原样保留，不再重复追加。
    /// </summary>
    public string DisplayVersion => HasPrereleaseTag(Version) || BuildType == BuildChannel.Release
        ? Version
        : string.Concat(Version, "-dev");

    /// <summary>工作区状态的可读文本（Clean / Modified / Unknown）。</summary>
    public string GitStatusText => GitStatus switch
    {
        GitWorkingTreeState.Clean => "Clean",
        GitWorkingTreeState.Modified => "Modified",
        _ => "Unknown",
    };

    /// <summary>构建渠道的可读文本（Development / Release）。</summary>
    public string BuildTypeText => BuildType == BuildChannel.Release ? "Release" : "Development";

    /// <summary>
    /// 从构建脚本写入的原始字符串构造。构建产物里都是普通字符串常量，
    /// 解析与降级规则集中在这里，便于回归测试覆盖。
    /// </summary>
    public static BuildInfo FromRaw(
        string? version,
        string? buildId,
        string? gitCommit,
        string? gitStatus,
        string? buildType) =>
        new(
            version,
            buildId,
            gitCommit,
            ParseGitStatus(gitStatus),
            ParseBuildType(buildType));

    /// <summary>多行构建信息，用于启动日志与「复制」按钮。</summary>
    public string ToDiagnosticText() => string.Join(
        Environment.NewLine,
        $"Version    : {Version}",
        $"Build ID   : {BuildId}",
        $"Git Commit : {GitCommit}",
        $"Git Status : {GitStatusText}",
        $"Build Type : {BuildTypeText}");

    private static string NormalizeVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();

    private static string NormalizeBuildId(string? buildId) =>
        string.IsNullOrWhiteSpace(buildId) ? UnknownValue : buildId.Trim();

    /// <summary>
    /// 只接受十六进制哈希。Git 命令失败时输出的是「fatal: ...」之类的文本，
    /// 直接写进程序里毫无意义，这里统一降级成 Unknown。
    /// </summary>
    private static string NormalizeCommit(string? gitCommit)
    {
        if (string.IsNullOrWhiteSpace(gitCommit))
        {
            return UnknownValue;
        }

        var trimmed = gitCommit.Trim();
        if (trimmed.Length is < 4 or > 64)
        {
            return UnknownValue;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return UnknownValue;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static GitWorkingTreeState ParseGitStatus(string? gitStatus) =>
        gitStatus?.Trim().ToLowerInvariant() switch
        {
            "clean" => GitWorkingTreeState.Clean,
            "modified" => GitWorkingTreeState.Modified,
            _ => GitWorkingTreeState.Unknown,
        };

    /// <summary>只有明确写着 Release 才算发布构建，其余一律按开发构建处理。</summary>
    private static BuildChannel ParseBuildType(string? buildType) =>
        string.Equals(buildType?.Trim(), "Release", StringComparison.OrdinalIgnoreCase)
            ? BuildChannel.Release
            : BuildChannel.Development;

    private static bool HasPrereleaseTag(string version)
    {
        var separator = version.IndexOf('-');
        return separator > 0 && separator < version.Length - 1;
    }
}
