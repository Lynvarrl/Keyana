using Keyana.Core;

namespace Keyana.App;

/// <summary>
/// 运行时读取构建信息的唯一入口。
///
/// 具体数值由 scripts/build-info.ps1 在构建阶段写进 BuildMetadata，随程序集一起编译：
/// 版本号来自 Keyana.App.csproj 的 &lt;Version&gt;，Build ID 是那次构建的时刻，
/// Git 提交与工作区状态取自构建时的仓库状态。运行期不访问 Git，也不读任何外部文件，
/// 所以打包后的独立 EXE 同样能报出自己的身份，且同一份产物每次启动结果都一致。
/// </summary>
internal static class BuildInfoProvider
{
    public static BuildInfo Current { get; } = BuildInfo.FromRaw(
        BuildMetadata.Version,
        BuildMetadata.BuildId,
        BuildMetadata.GitCommit,
        BuildMetadata.GitStatus,
        BuildMetadata.BuildType);
}
