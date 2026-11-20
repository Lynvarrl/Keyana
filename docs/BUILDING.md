# 构建与发布

## 环境

- Windows x64；
- .NET 10 SDK；
- PowerShell。

仓库的 `global.json` 指定 .NET SDK 10.0.400，并允许使用同一功能带的更新补丁版本。

## 构建

```powershell
dotnet restore .\Keyana.slnx
dotnet build .\Keyana.slnx -c Release --no-restore
```

## 回归测试

测试项目是一个无第三方测试框架的控制台程序：

```powershell
dotnet run --project .\tests\Keyana.Core.Tests -c Release --no-build
```

进程返回码为 0 表示全部通过。

## 发行包

```powershell
.\scripts\publish.ps1
```

脚本会生成（版本号取自 `src/Keyana.App/Keyana.App.csproj` 的 `<Version>`）：

- `Keyana-V<版本号>-full-win-x64.zip`：包含 .NET 桌面运行时；
- `Keyana-V<版本号>-lite-win-x64.zip`：依赖 x64 .NET 10 Desktop Runtime；
- `SHA256SUMS.txt`：两个 ZIP 的 SHA-256。

## 构建信息

每次构建都会由 `scripts/build-info.ps1` 生成 `obj/<配置>/BuildMetadata.g.cs` 并编译进程序集：

| 字段 | 来源 |
| --- | --- |
| Version | `Keyana.App.csproj` 的 `<Version>`（唯一来源，脚本不做改写） |
| Build ID | 构建时刻，格式 `YYYYMMDD-HHMMSS` |
| Git Commit | 构建时 `git rev-parse --short HEAD` |
| Git Status | 构建时 `git status --porcelain` 是否为空，`Clean` / `Modified` |
| Build Type | `Release` 配置为 `Release`，其余为 `Development` |

- Build ID 在构建阶段固定：同一份产物启动多少次都是同一个值，打包后的 EXE 不需要 Git；
- 源码不在 Git 仓库、构建机没有 Git 时，Git 相关字段降级为 `unknown` / `Unknown`，构建照常完成；
- 构建信息在设置页「关于」卡片右下角展示，并在每次启动时写入 `Data\startup.log`；
- 侧边栏左下角的版本号取自同一份数据，保留原有的 `v` 前缀：Release 显示 `v1.3.4`，
  Development 显示 `v1.3.4-dev`，版本号本身带预发布标识（如 `v1.4.0-alpha.1`）时原样保留。

