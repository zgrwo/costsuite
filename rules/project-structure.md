# 项目结构规范（Project Structure）

> **日期**: 2026-07-12  
> **用途**: Sprint 0 骨架搭建的工作输入、全团队统一的文件路由与命名规范  
> **配套文档**: [specification.md](./specification.md)（架构定义）、[refactoring-plan.md](./refactoring-plan.md)（重构计划）

---

## 1. 设计原则

| 原则 | 说明 |
|------|------|
| **一架构一层一程序集** | 六层架构中的每一层映射为一个 `.csproj`，依赖方向严格单向 |
| **Core 零外部依赖** | `BomAddIn.Core` 不引用 Excel、数据库、网络库 — 纯 C# 业务逻辑，可独立单测 |
| **UI 薄，逻辑厚** | UI 层仅做展示和交互委托；BLL 持有全部业务规则 |
| **Excel 依赖关在顶层** | 仅 `BomAddIn`（宿主）引用 Excel-DNA 和 COM Interop；其他层完全不知 Excel 存在 |
| **测试与源码镜像** | `tests/` 目录结构平行于 `src/`，命名加 `.Tests` 后缀 |
| **约定优于配置** | 命名空间、文件夹、文件名遵循统一模式，减少 `using` 别名和路径猜测 |

---

## 2. 解决方案总览

```
BomAddIn.sln
│
├── src/                                    ← 源代码
│   ├── BomAddIn/                           ← Excel-DNA 宿主（UI + Bridge + UDF）
│   ├── BomAddIn.Core/                      ← 业务逻辑 + 差异引擎 + 领域模型
│   ├── BomAddIn.Data/                      ← 数据访问 + 同步 + 缓存 + 分析
│   │   ├── Analysis/                    ← 🆕 DuckDB 分析查询
│   └── BomAddIn.Infrastructure/            ← 横切关注点（日志/安全/审计/配置）
│
├── tests/                                  ← 测试项目
│   ├── BomAddIn.UnitTests/                 ← xUnit 单元测试
│   ├── BomAddIn.IntegrationTests/          ← TestContainers 集成测试
│   ├── BomAddIn.ThreadingTests/            ← 线程安全压力测试
│   └── BomAddIn.PerformanceTests/          ← BenchmarkDotNet 性能测试
│
├── tools/                                  ← 独立工具
│   ├── BomAddIn.Diagnostic/                ← 环境诊断工具（控制台应用）
│   └── MemoryDiagnostic/                   ← 内存诊断工具
│
├── database/                               ← 数据库文件 (dev + prod 环境隔离)
│   ├── dev/
│   └── prod/
│
├── build/                                  ← 构建与打包
│   └── scripts/                            ← 构建/签名/发布脚本
│
├── template/                               ← 种子数据 CSV 模板
│   ├── Materials.csv
│   ├── BomStructures.csv
│   ├── Prices.csv
│   └── ...                                 ← 其他数据表模板
│
├── rules/                                  ← 规范文档（SSOT）
│   ├── context.md                          ← 项目上下文（术语表）
│   ├── project-structure.md                ← 项目结构规范（本文档）
│   ├── api-reference.md                    ← UDF API 权威合约
│   ├── user-manual.md                      ← 最终用户手册
│   ├── specification.md                    ← 技术规格说明书
│   ├── refactoring-plan.md                 ← 重构计划
│   ├── code-review-prompt.md               ← 审查模板
│   └── documentation.md                    ← 文档职责规范
│
├── skills/                                 ← 可复用开发模式
│   ├── excel-dna-threading.md
│   ├── excel-dna-wpf-dashboard.md
│   ├── bom-modeling-patterns.md
│   ├── excel-dna-di-startup.md
│   ├── excel-udf-best-practices.md
│   ├── offline-first-architecture.md
│   ├── architecture-reviewer.md
│   ├── refactoring-guardian.md
│   └── project-plan-review.md
│
├── .github/workflows/                      ← CI/CD pipeline
│   └── ci.yml
├── logs/                                   ← 运行日志（gitignored）
├── agents.md                               ← 项目宪法（AI 协作行为准则）
├── .editorconfig
├── Directory.Build.props
├── LICENSE
├── BomAddIn.sln
└── readme.md
```

---

## 2.1 文档分工（5S：单源真理）

> **一个事实只在一处定义。** 下表规定了每份文档的职责边界。发现同一事实出现在两个文档中 → 删除非权威那份，改为链接。

### 核心文档（读写，持续更新）

| 文档 | 职责（它是...的唯一权威来源） | 不负责（去别处找） | 更新触发条件 |
|------|---------------------------|-------------------|-------------|
| **rules/context.md** | 项目上下文（问题域、用户、技术选择理由、术语表） | 所有文档的“为什么” | 新成员（5 分钟阅读） | 重大定位调整时 |
| **project-structure.md** | 程序集清单、依赖关系图、文件夹路由、命名空间约定、文件命名规则、.dna 配置、文档分工表（本文） | 架构设计 → specification.md；重构排期 → refactoring-plan.md | 新增/合并程序集、命名规范变更 |
| **api-reference.md** | UDF 函数签名、参数类型与约束、返回值语义、错误码、使用示例 | 函数设计意图 → specification.md §8；用户场景 → user-manual | 新增 UDF、修改函数签名或行为 |
| **user-manual.md** | 安装步骤、功能操作指南、角色权限说明、故障排查、快速参考 | 技术细节 → specification.md；函数语法 → api-reference；代码开发 → skills/ | 新功能上线、UI 变更、新增常见问题 |

### 补充文档

| 文档 | 性质 | 何时读 |
|------|------|--------|
| **rules/specification.md** | 技术规格说明书（架构设计、数据模型、安全策略） | 追溯架构设计决策时查阅 |
| **rules/refactoring-plan.md** | 重构计划（阶段划分、验收标准、回滚策略） | 执行重构任务时查阅 |
| **rules/code-review-prompt.md** | 代码审查模板 | 系统性审查时查阅 |
| **rules/documentation.md** | 文档职责规范 | 新增/修改文档时查阅 |

### skills/ 目录

| 定位 | 说明 |
|------|------|
| **不是规格** | Skill 不定义“是什么”（→ specification.md），不排期“何时做”（→ refactoring-plan.md） |
| **是模式** | Skill 是经过验证的代码范式——“怎么写”、“陷阱是什么”、“检查清单” |
| **来源于外部** | 每个 skill 标注了借鉴的 GitHub 项目 |
| **被 agents.md 引用** | 任务路由表中的“应该读”列指向 skill |

### 主目录文件

| 文件 | 定位 |
|------|------|
| **agents.md** | 项目宪法（AI 协作行为准则）。不重复定义事实——通过任务路由表指引到上述文档 |
| **.gitignore** | 排除规则（使用仓库根 .gitignore） |

### 违反信号

> 如果你在写文档时出现以下情况，停下来检查上表：

| 信号 | 意味着 |
|------|--------|
| "这个规则在 spec 里写过了，但 plan 里也提了一句..." | 🔴 删除 plan 中的重复，改为 `见 spec §X` |
| "api-reference 里的这个参数说明应该同步到 spec..." | 🔴 不同步。删除 spec 中的重复，保留 api-reference 为唯一权威 |
| "user-manual 需要描述这个函数的行为..." | 🟢 概述+链接即可。详细语法已在 api-reference |
| "这个 skill 在重复 spec 中的架构定义..." | 🔴 删除 skill 中的架构描述，保留代码范式 |

---

## 3. 程序集清单与依赖方向

### 3.1 项目一览

| 项目 | 程序集名 | 类型 | 目标框架 | 说明 |
|------|---------|------|---------|------|
| `src/BomAddIn` | `BomAddIn.dll` | Excel-DNA Add-in | `net472` | UI + Bridge + UDF 入口。唯一引用 Excel-DNA 的程序集 |
| `src/BomAddIn.Core` | `BomAddIn.Core.dll` | 类库 | `netstandard2.0` | 领域模型、BLL 服务、差异引擎、事件契约 |
| `src/BomAddIn.Data` | `BomAddIn.Data.dll` | 类库 | `netstandard2.0` | Repository、缓存、同步适配器、DuckDB 分析、SQLite 迁移 |
| `src/BomAddIn.Infrastructure` | `BomAddIn.Infrastructure.dll` | 类库 | `netstandard2.0` | AppLogger、加密、审计、配置、网络检测 |
| `tests/BomAddIn.UnitTests` | — | xUnit | `net472` / `net8.0` | 单元测试（Mock 全部外部依赖） |
| `tests/BomAddIn.IntegrationTests` | — | xUnit | `net472` / `net8.0` | 集成测试（真实数据库 via TestContainers） |
| `tests/BomAddIn.ThreadingTests` | — | 自定义 | `net472` | Excel COM 线程安全压力测试 |
| `tests/BomAddIn.PerformanceTests` | — | BenchmarkDotNet | `net472` | 性能基准与回归测试 |
| `tools/BomAddIn.Diagnostic` | — | 控制台 | `net472` | 运行环境诊断工具 |

> **框架说明**: 宿主项目使用 `net472`（Excel-DNA 最佳兼容性）。类库使用 `netstandard2.0` 以实现最大兼容性，同时可被 `net472` 和未来 `net8.0` 宿主引用。

### 3.2 依赖关系图（严格单向）

```text
┌──────────────────────────────────┐
│            BomAddIn               │  ← Excel-DNA 宿主
│      (UI + Bridge + UDF)          │    引用: ExcelDna.Integration
└──────────────┬───────────────────┘        ExcelDna.Interop
               │ 引用                         Microsoft.Office.Interop.Excel
               ▼                            System.Windows.Forms (TaskPane)
┌──────────────────────────────────┐        PresentationFramework (WPF)
│         BomAddIn.Core             │
│    (BLL + Variance Engine)        │  ← 纯业务逻辑
└──────────────┬───────────────────┘    引用: (无外部依赖)
               │ 引用                      仅依赖 .NET BCL
               ▼
┌──────────────────────────────────┐
│         BomAddIn.Data             │
│    (DAL + Repository + Sync)       │  ← 数据访问
└──────────────┬───────────────────┘    引用: Dapper, System.Data.SQLite
               │ 引用                     DuckDB.NET.Data
               ▼
┌──────────────────────────────────┐
│     BomAddIn.Infrastructure       │
│  (Logging + Security + Audit)     │  ← 横切关注点
└──────────────────────────────────┘    引用: BCrypt.Net-Next
                                              Microsoft.Extensions.DependencyInjection
                                              System.Security.Cryptography.ProtectedData
```

**依赖规则**（CI 中通过架构测试强制执行）：

| 规则 | 说明 |
|------|------|
| **单向依赖** | 只能从上往下引用，禁止反向 |
| **Core = 纯净室** | `BomAddIn.Core` 不得引用 `ExcelDna*`、`System.Data*` 等 |
| **Data 不知 UI** | `BomAddIn.Data` 不得引用 `BomAddIn` |
| **Infra 不知业务** | `BomAddIn.Infrastructure` 不得引用 `Core` 和 `Data` |
| **测试可跨层** | 测试项目可以引用任何 `src/` 项目 + Mock/TestContainers 等 |

---

## 4. 文件夹路由（完整层级）

### 4.1 `src/BomAddIn/` — Excel-DNA 宿主

```
BomAddIn/
├── Properties/
│   └── AssemblyInfo.cs
├── Bridge/                                   # Bridge Layer
│   ├── IExcelThreadDispatcher.cs
│   ├── ExcelThreadDispatcher.cs
│   ├── IVersionAdapter.cs
│   └── VersionAdapter.cs
├── Ribbon/
│   └── RibbonController.cs                    # ExcelRibbon 派生类（XML 嵌入资源）
├── UI/
│   ├── TaskPane/
│   │   ├── BomTaskPane.xaml
│   │   └── BomTaskPane.xaml.cs
│   └── Import/
│       └── FileImportService.cs               # Excel/CSV 文件解析（EPPlus）
├── Dashboard/
│   ├── DashboardWindow.xaml
│   ├── DashboardWindow.xaml.cs
│   ├── DashboardBootstrapper.cs
│   ├── DashboardViewModel.cs
│   └── ClickTwiceFix.cs
├── UDF/
│   ├── Container.cs
│   ├── Functions/
│   │   ├── BomQueryFunctions.cs               # BOMEXPAND, BOMCOST
│   │   ├── DataQueryFunctions.cs              # PRICELOOKUP, INVENTORYQTY
│   │   ├── SystemFunctions.cs                 # SYNCSTATUS
│   │   └── VarianceFunctions.cs               # VARIANCECHECK, ALERTCHECK
│   └── Helpers/
│       └── UdfParameterParser.cs
├── Bootstrap/
│   ├── AutoOpen.cs                           # Excel-DNA AutoOpen 入口 + DI 容器初始化
│   ├── ServiceConfigurator.cs                # DI 注册（按层分组的方法）
│   └── LogConfigurator.cs                    # 日志配置占位
├── BomAddIn.csproj
├── BomAddIn-AddIn.dna                        # 32 位 Excel-DNA 配置
└── BomAddIn-AddIn64.dna                      # 64 位 Excel-DNA 配置
```

### 4.2 `src/BomAddIn.Core/` — 业务逻辑 + 差异引擎

```
BomAddIn.Core/
├── Models/                                   # 业务层特有模型
│   ├── Alert.cs
│   └── VarianceResult.cs
├── Services/                                 # BLL 服务接口 + 实现
│   ├── IBomService.cs / BomService.cs
│   ├── IVarianceService.cs / VarianceService.cs
│   ├── IAuthService.cs / AuthService.cs
│   ├── ISyncService.cs / SyncService.cs
│   ├── IApprovalService.cs / ApprovalService.cs
│   ├── IAuditService.cs / AuditService.cs
│   ├── IAuthorizationService.cs / AuthorizationService.cs
│   ├── IBomExcelImporter.cs / BomExcelImporter.cs
│   ├── IConfigService.cs / ConfigService.cs
│   ├── ISnapshotService.cs / SnapshotService.cs
│   ├── SeedDataGenerator.cs
│   ├── IVarianceCalculator.cs / VarianceCalculator.cs
│   └── IAlertEvaluator.cs / AlertEvaluator.cs
└── BomAddIn.Core.csproj
```

> ℹ️ v1.1 变更：原 `Events/` 目录（4 个领域事件类）已随事件总线移除（YAGNI，零生产者/零消费者），
> 见 [specification.md §2.4](./specification.md#24-事件总线v11-已移除--yagni)。

### 4.3 `src/BomAddIn.Data/` — 数据访问层

```
BomAddIn.Data/
├── Repositories/
│   ├── IMaterialRepository.cs / MaterialRepository.cs
│   ├── IBomNodeRepository.cs / BomNodeRepository.cs
│   ├── IBomVersionRepository.cs / BomVersionRepository.cs
│   ├── IPriceRecordRepository.cs / PriceRecordRepository.cs
│   ├── IInventoryRecordRepository.cs / InventoryRecordRepository.cs
│   ├── IOrderRecordRepository.cs / OrderRecordRepository.cs
│   ├── ICapacityRecordRepository.cs / CapacityRecordRepository.cs
│   ├── IUserRepository.cs / UserRepository.cs
│   ├── IUserTokenRepository.cs / UserTokenRepository.cs
│   ├── IAuditLogRepository.cs / AuditLogRepository.cs
│   ├── ISnapshotRepository.cs / SnapshotRepository.cs
│   ├── IAppConfigRepository.cs / AppConfigRepository.cs
│   ├── ISyncLogRepository.cs / SyncLogRepository.cs
│   └── IDataSnapshotRepository.cs / DataSnapshotRepository.cs
├── Caching/
│   ├── ICacheProvider.cs
│   └── MemoryCacheProvider.cs
├── Analysis/
│   ├── IBomAnalysisProvider.cs
│   └── BomAnalysisProvider.cs
├── Sync/
│   ├── IErpAdapter.cs
│   └── SimulatedErpAdapter.cs
├── Connection/
│   ├── IDbConnectionFactory.cs
│   └── SqliteConnectionFactory.cs
├── Migration/
│   └── DatabaseMigrator.cs
├── Migrations/
│   ├── S001_InitialSchema.sql
│   ├── S002_ApprovalWorkflow.sql
│   └── S003_PerformanceIndexes.sql
└── BomAddIn.Data.csproj
```

### 4.4 `src/BomAddIn.Infrastructure/` — 横切关注点

```
BomAddIn.Infrastructure/
├── Models/                                   # 领域模型（POCO，EX-001 豁免）
│   ├── Material.cs / BomNode.cs / BomVersion.cs
│   ├── PriceRecord.cs / InventoryRecord.cs
│   ├── OrderRecord.cs / CapacityRecord.cs
│   ├── User.cs / UserToken.cs / AuditLog.cs
│   ├── SyncLog.cs / DataSnapshot.cs / AppConfig.cs
│   └── Enums/
│       ├── VersionState.cs / UserRole.cs / AlertSeverity.cs
│       └── BomOperation.cs
├── Security/
│   ├── IEncryptionProvider.cs / DpapiEncryptionProvider.cs
│   └── IPasswordHasher.cs / BCryptPasswordHasher.cs
├── Config/
│   ├── IConfigProvider.cs
│   ├── AppConfigProvider.cs
│   └── EnvironmentManager.cs
├── Network/
│   ├── INetworkMonitor.cs
│   └── NetworkMonitor.cs
└── BomAddIn.Infrastructure.csproj
```

### 4.5 `tests/` — 测试项目

```
tests/
├── BomAddIn.UnitTests/                       # 24 单元测试文件（扁平结构）
│   ├── AuthServiceTests.cs
│   ├── AuthorizationServiceTests.cs
│   ├── BomServiceTests.cs / BomServiceEdgeCaseTests.cs
│   ├── VarianceServiceTests.cs / VarianceCalculatorTests.cs
│   ├── SyncServiceTests.cs
│   ├── ApprovalServiceTests.cs / ApprovalServiceEdgeCaseTests.cs
│   ├── AuditServiceTests.cs
│   ├── SnapshotServiceTests.cs
│   ├── AlertEvaluatorTests.cs
│   ├── BomQueryFunctionsTests.cs / DataQueryFunctionsTests.cs
│   ├── VarianceFunctionsTests.cs / SystemFunctionsTests.cs
│   ├── BomExcelImporterTests.cs / BomClosureTableTests.cs
│   ├── AesEncryptionProviderTests.cs / NetworkMonitorTests.cs
│   ├── EdgeCaseTests.cs / LargeDatasetTests.cs
│   └── BomAnalysisProviderConcurrencyTests.cs
├── BomAddIn.IntegrationTests/                # 11 集成测试文件
│   ├── MaterialRepositoryTests.cs
│   ├── BomNodeRepositoryTests.cs
│   ├── BomVersionRepositoryTests.cs
│   ├── PriceRecordRepositoryTests.cs
│   ├── AuditLogRepositoryTests.cs
│   ├── UserRepositoryTests.cs
│   ├── ConfigServiceTests.cs / MigrationTests.cs
│   ├── DuckDBCompatibilityTests.cs / SeedDataGeneratorTests.cs
│   └── SqliteTestFixture.cs
├── BomAddIn.ThreadingTests/                  # 线程安全压力测试
│   ├── ThreadSafetyTests.cs
│   ├── ThreadStressTests.cs
│   └── UdfParameterParserTests.cs
└── BomAddIn.PerformanceTests/
    ├── BomExpansionBenchmarks.cs
    └── Program.cs
```

### 4.6 `database/` — 数据库文件

```
database/
├── dev/
│   └── bom_data_dev.sqlite                    # 开发环境数据库
└── prod/
    └── bom_data.sqlite                        # 生产环境数据库
```

> **说明**: 迁移脚本位于 `src/BomAddIn.Data/Migrations/`，由 `DatabaseMigrator`（System.Data.SQLite）自动执行。

---

## 4.7 数据库

> **数据模型权威定义**: 见表结构、ER 图、外键关系 → [specification.md §4](./specification.md#4-数据模型)。  
> **DDL 实现**: 见 `src/BomAddIn.Data/Migrations/S001_InitialSchema.sql`。

### 4.7.1 种子数据规模 (500 物料基准)

| 表 | 行数 | 说明 |
|------|------|------|
| Materials | 500 | 5% 根节点, BOM 深度 1-5 层 |
| BomStructures | ~540 | 树形结构, 扇出 3-8, 共享件 ~14% |
| BomVersions | ~80 | Draft/Obsolete 节点版本链 |
| Prices | 6,000 | 500 物料 × 12 月历史 |
| Inventories | 6,000 | 500 物料 × 12 月历史 |
| Suppliers | 20 | 20 家供应商 (中国产业链) |
| Orders | ~150 | 30% 物料有在途订单 |
| Capacities | 7 | 7 个工作中心 |
| Estimates | 15 | 15 个根物料成本估算 |
| SyncLogs | 5 | Full/Incremental 同步记录 |
| Users | 2 | admin / viewer |
| AppConfig | 6 | 环境/同步/缓存/阈值配置 |

---

### 4.8 `build/` — 构建与打包

```
build/
├── BomAddIn-AddIn.dna                       # Excel-DNA 32 位配置
├── BomAddIn-AddIn64.dna                     # Excel-DNA 64 位配置
├── packaging/
│   └── BomAddIn.nuspec                       # NuGet 包描述（可选）
└── scripts/
    ├── build.ps1                             # 构建 + 运行测试
    └── sign.ps1                              # Authenticode 签名
```

---

## 5. 命名空间约定

| 层级 | 命名空间根 | 示例 |
|------|-----------|------|
| **UI** | `BomAddIn.UI` | `BomAddIn.UI.Ribbon`, `BomAddIn.UI.Dashboard.Views` |
| **Bridge** | `BomAddIn.Bridge` | `BomAddIn.Bridge` |
| **UDF** | `BomAddIn.Functions` | `BomAddIn.Functions.Bom` |
| **Bootstrap** | `BomAddIn` | `BomAddIn`（AutoOpen 在根命名空间，Excel-DNA 要求） |
| **BLL** | `BomAddIn.Core.Services` | `BomAddIn.Core.Services` |
| **Engine** | `BomAddIn.Core.Engine` | `BomAddIn.Core.Engine.Dimensions` |
| **Models** | `BomAddIn.Core.Models` | `BomAddIn.Core.Models.Enums` |
| **Events** | `BomAddIn.Core.Events` | `BomAddIn.Core.Events` |
| **DAL** | `BomAddIn.Data` | `BomAddIn.Data.Repositories`, `BomAddIn.Data.Caching` |
| **Infra** | `BomAddIn.Infrastructure` | `BomAddIn.Infrastructure.Security` |
| **Tests** | `BomAddIn.UnitTests` 等 | `BomAddIn.UnitTests.Core.Services` |

**规则**:
- 命名空间与文件夹路径一一对应（IDE 自动推断）
- `BomAddIn` 根命名空间仅用于 `AutoOpen` 和 Excel-DNA 要求的顶级函数
- `Models` 是纯 POCO — 无方法、无依赖、全程序集共享
- `Events` 定义在 Core 层 — 所有层都可以引用消息契约

---

## 6. 文件命名与组织约定

### 6.1 接口与实现

```
IBomService.cs        ← 接口（I 前缀）
BomService.cs         ← 默认实现（同名去 I）
IBomServiceTests.cs   ← 对应的单元测试
```

- 接口与实现放在同一文件夹（遵循 [ASP.NET Core 惯例](https://github.com/dotnet/aspnetcore)）
- 一个接口通常只有一个默认实现，避免 `Impl` 后缀
- 若需要多个实现，用功能命名：`ErpRestAdapter.cs`, `ErpGrpcAdapter.cs`

### 6.2 一个文件一个类型

```
✅ BomService.cs         → 1 class: BomService（可能含 private 辅助类）
❌ Services.cs           → 不要把所有服务塞一个文件
```

- Exception: `Enums/VersionState.cs` 可含多个相关枚举

### 6.3 迁移脚本命名

```
S{序号}_{描述}.sql

✅ S001_InitialSchema.sql
✅ S005_AddPerformanceIndexes.sql
❌ 001.sql
❌ migration_1.sql
```

- 序号递增、不跳号、不复用
- 已执行的迁移永远不修改（只增不改，幂等性核心原则）

### 6.4 测试文件命名

```
{被测类名}Tests.cs

✅ BomServiceTests.cs
✅ VarianceCalculatorTests.cs
✅ ExcelThreadDispatcherStressTests.cs      ← 线程安全测试加 "Stress" 后缀
✅ BomExpansionBenchmarks.cs                ← 性能测试加 "Benchmarks" 后缀
```

---

## 7. Excel-DNA 特殊约定

### 7.1 `.dna` 配置文件

两个 `.dna` 文件（32/64 位）放在 `src/BomAddIn/`，构建时复制到输出：

```xml
<!-- BomAddIn-AddIn.dna -->
<DnaLibrary Name="BomAddIn" RuntimeVersion="v4.0">
  <ExternalLibrary Path="BomAddIn.dll" Pack="true" />
  <ExternalLibrary Path="BomAddIn.Core.dll" Pack="true" />
  <ExternalLibrary Path="BomAddIn.Data.dll" Pack="true" />
  <ExternalLibrary Path="BomAddIn.Infrastructure.dll" Pack="true" />
  <!-- NuGet 依赖由 ExcelDnaPack 自动处理 -->
</DnaLibrary>
```

### 7.2 UDF 函数注册

- 所有 `[ExcelFunction]` 放在 `src/BomAddIn/UDF/` 目录
- 每个文件一类函数（如 `BomFunctions.cs` 只含 BOM 相关 UDF）
- UDF 方法本身是薄壳 — 立即委托给 `BomAddIn.Core` 服务：

```csharp
[ExcelFunction(Name = "BOMEXPAND", IsThreadSafe = true)]
public static object[,] BomExpand(string itemCode, DateTime? asOfDate = null)
{
    var service = Container.Resolve<IBomService>();
    return service.Expand(itemCode, asOfDate ?? DateTime.Today).ToArray2D();
}
```

### 7.3 `AutoOpen` 方法

- 位置: `src/BomAddIn/Bootstrap/AutoOpen.cs`
- 职责极简: (1) 初始化 DI (2) 启动探针自检 (3) 初始化日志
- 必须 `catch` 所有异常并以可读方式呈现给用户（Excel 弹窗）

---

## 8. 共享构建配置

### 8.1 `Directory.Build.props`

放在仓库根目录，统一所有项目的 MSBuild 属性：

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Authors>CostSuite Team</Authors>
    <Copyright>© 2026</Copyright>
  </PropertyGroup>
</Project>
```

### 8.2 `.editorconfig`

统一代码风格（缩进、命名规则、`var` 使用策略等），所有 IDE（VS / Rider / VS Code）共享。

### 8.3 `.gitignore`

标准 Visual Studio `.gitignore` + Excel-DNA 特定补充：

```
# Excel-DNA
*.xll
*.dna.config
packed/

# 本地 SQLite
*.sqlite
*.sqlite-shm
*.sqlite-wal

# 用户配置（不提交本地覆盖）
user.config
*.user
```

---

## 9. 与六层架构的映射关系

| 架构层 | 对应项目 | 关键文件夹 |
|--------|---------|-----------|
| **表示层 (UI)** | `BomAddIn` | `UI/Ribbon/`, `UI/TaskPane/`, `UI/Dashboard/` |
| **桥接层 (Bridge)** | `BomAddIn` | `Bridge/` |
| **业务逻辑层 (BLL)** | `BomAddIn.Core` | `Services/` |
| **差异计算引擎** | `BomAddIn.Core` | `Services/`（VarianceCalculator, AlertEvaluator） |
| **数据访问层 (DAL)** | `BomAddIn.Data` | `Repositories/`, `Caching/` |
| **基础设施层** | `BomAddIn.Infrastructure` | 全部 |

> **说明**: Bridge 层虽然在架构图中独立，但由于其对 Excel-DNA 的强依赖，与 UI 层同属一个程序集 `BomAddIn`。这在 Excel-DNA 生态中是标准做法 —— 所有需要 Excel-DNA API 的代码必须在同一个 AppDomain 的主程序集内。

---

## 10. Sprint 0 骨架搭建清单

按照本文档创建项目结构时，按以下顺序执行：

| 步骤 | 操作 | 产物 |
|------|------|------|
| 1 | 创建 `BomAddIn.sln` | 空解决方案 |
| 2 | 创建 4 个 `src/` 项目 + 4 个 `tests/` 项目 | 8 个 `.csproj` |
| 3 | 配置项目引用（按 §3.2 依赖图） | 单向依赖链 |
| 4 | 添加 `Directory.Build.props` + `.editorconfig` | 统一构建配置 |
| 5 | 添加 NuGet 包（Dapper、BCrypt 等） | `packages.config` 或 `PackageReference` |
| 6 | 创建 `Bridge/ExcelThreadDispatcher.cs` + 接口 | Sprint 0 探针 P-0.1 |
| 7 | 配置 `.dna` 文件 | Excel-DNA 加载链路 |
| 8 | 编写 `Bootstrap/AutoOpen.cs` (DI + 健康检查) | 骨架可运行 |
| 9 | 验证: `BomAddIn.xll` 加载成功，Ribbon 显示 "BOM Suite" 标签页 | ✅ Sprint 0 骨架 DONE |
| 10 | 初始化 `db/migrations/S001_InitialSchema.sql` | Sprint 1 就绪 |

---

## 11. 常见问题

**Q: 为什么不把 Bridge 层拆成独立程序集？**

A: Bridge 层需要调用 `ExcelAsyncUtil.QueueAsMacro`，这要求与 Excel-DNA 在同一 AppDomain。独立程序集只会增加复杂的跨程序集 COM 封送，没有明显收益。

**Q: 测试项目为什么也用 `net472`？**

A: 线程安全测试需要加载 Excel-DNA 并实际调用 COM API，必须在与宿主相同的运行时下执行。单元测试和性能测试可以用 `net8.0`（更快），但建议保持一致以简化 CI 配置。

**Q: 为什么不直接用 Clean Architecture 的 UseCase/Entity 命名？**

A: 六层架构是原文档的设计决策。本文档保持与 specification.md 的术语一致（BLL / DAL / Engine），降低团队的认知转换成本。可逐 Sprint 渐进式重构为 Clean Architecture 术语。
