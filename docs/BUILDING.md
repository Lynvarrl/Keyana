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

## 开发版

日常调试用 `dev.ps1`，它等价于 `publish.ps1 -Channel Development`，构建完直接启动：

```powershell
.\scripts\dev.ps1            # 构建并启动
.\scripts\dev.ps1 -NoLaunch  # 只构建
```

产物在 `artifacts\dev`：Debug 配置、框架依赖、保留 pdb，不打 zip、不写校验和。
Build Type 为 `Development`，侧边栏版本号下方会显示本次构建的 Build ID。
`artifacts\dev\Data` 跨构建保留（设置、主题、已采集的统计都在里面），想要干净环境时手动删掉它。

开发版的单实例锁与正式版分开（`Local\Keyana.SingleInstance.development.v1`），
可以和正式版同时运行，退出信号也不会误杀正式版。正式版沿用历史名称，
升级瞬间新旧实例仍能互相识别。

## 发行包

```powershell
.\scripts\publish.ps1
```

脚本会生成（版本号取自 `src/Keyana.App/Keyana.App.csproj` 的 `<Version>`）：

- `Keyana-V<版本号>-full-win-x64.zip`：包含 .NET 桌面运行时；
- `Keyana-V<版本号>-lite-win-x64.zip`：依赖 x64 .NET 10 Desktop Runtime；
- `SHA256SUMS.txt`：两个 ZIP 的 SHA-256。

渠道由 `-Channel` 决定，默认 `Release`：

```powershell
.\scripts\publish.ps1 -Channel Development   # 同 dev.ps1 -NoLaunch，只出 artifacts\dev
```

`<Version>` 只写数字（如 `1.3.5`）。`-dev` 是构建渠道派生的后缀，写进版本号会让
Release 构建也带着 `-dev` 发出去；`-Channel Release` 遇到这种版本号会直接报错。

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
- 侧边栏左下角的版本号取自同一份数据，保留原有的 `v` 前缀：Release 显示 `v1.3.5`，
  Development 显示 `v1.3.5-dev`，版本号本身带预发布标识（如 `v1.4.0-alpha.1`）时原样保留；
- Development 构建在版本号下方额外显示一行 Build ID（等宽字体、三级文字色），
  用来确认运行中的进程是不是刚编出来的那个；Release 构建不显示这一行，侧边栏保持原样。

