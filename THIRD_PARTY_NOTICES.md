# 第三方组件与许可证

Keyana 本身以 MIT 许可发布，完整文本见随包的 `LICENSE`。

发布包内同时包含以下许可证文件：

| 文件 | 说明 |
| --- | --- |
| `LICENSE` | Keyana 的 MIT 许可证 |
| `THIRD_PARTY_NOTICES.md` | 本文件 |
| `licenses/Apache-2.0.txt` | Apache License 2.0 全文 |
| `licenses/dotnet-runtime-LICENSE.txt` | .NET 运行时的 MIT 许可证（完整版随包分发运行时） |

## NuGet 依赖

| 组件 | 版本 | 许可证 |
| --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.11 | MIT |
| Microsoft.Data.Sqlite.Core | 10.0.11 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 |

SQLitePCLRaw 系列组件以 Apache-2.0 分发，完整文本见 `licenses/Apache-2.0.txt`。

## 随包分发的运行时与原生库

- **.NET 10 运行时**（仅完整版）：MIT，版权归 .NET Foundation and Contributors，
  文本见 `licenses/dotnet-runtime-LICENSE.txt`；
- **SQLite**（由 SQLitePCLRaw.lib.e_sqlite3 内置）：Public Domain，
  https://www.sqlite.org/copyright.html。

第三方组件仍分别受其原始许可证约束；NuGet 包元数据与上游仓库中的许可证文本是
判断具体分发义务的最终依据。
