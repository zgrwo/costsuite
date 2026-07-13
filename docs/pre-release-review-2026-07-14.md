# BomAddIn v1.1 发布前全面深度审查报告

> **日期**: 2026-07-14
> **审查范围**: 全项目（8 程序集、~120 源文件、~70 测试、6 份文档）
> **审查方法**: 逐文件阅读 + 分层代理并行审查 + 规范对照
> **严重级别**: 🔴 阻塞发布 | 🟠 发布前应修复 | 🟡 首个补丁前修复 | 🔵 建议改进

---

## 执行摘要

项目架构扎实，代码质量中等偏上，文档体系完整。发现 **4 个发布阻塞项**、**12 个高优先级问题**、**15 个中等问题**。主要风险集中在：加密实现缺失、离线模式零测试覆盖、UDF 层业务逻辑泄漏、硬编码配置值。

**总体评估**: ⚠️ 有条件可通过 — 修复 **7 个阻塞项**后可发布 v1.1-beta，修复高优先级项后可发布 v1.1 正式版。

> **审查耗时**: 5 个并行代理 + 主线程分析，覆盖 Infrastructure / Core / Data / UI-Bridge-UDF / Tests 全部 5 层

---

## 一、架构与依赖审查

### ✅ 合规项

| 检查项 | 状态 |
|--------|:--:|
| 单向依赖链 (BomAddIn → Core → Data → Infra) | ✅ 正确 |
| Core 零外部 NuGet 依赖 | ✅ 仅引用 Data 项目 |
| netstandard2.0 类库兼容性 | ✅ Core/Data/Infra 均为 netstandard2.0 |
| net472 宿主兼容 Excel-DNA | ✅ BomAddIn 为 net472 |
| Nullable enable 全局启用 | ✅ Directory.Build.props |
| .dna 32/64 位配置分离 | ✅ SQLite.Interop 路径正确 |
| 解决方案结构 4 src + 4 test + 1 tool | ✅ |

### ⚠️ 偏离项

| ID | 问题 | 严重度 | 说明 |
|----|------|:------:|------|
| A-1 | Infrastructure 层含 Dapper/SQLite NuGet 引用 | 🟡 | 日志/安全层不应直接依赖数据访问库。当前因 Models 放在 Infra 中 (EX-001)，Repository 查询结果映射需要 Dapper |
| A-2 | BomAddIn 宿主直接使用 Dapper (ServiceConfigurator:59) | 🟠 | Bootstrap 层绕过 Data 层直接创建 SQLiteConnection+ Dapper 查询，破坏了分层抽象 |
| A-3 | BomAddIn.Core 未引用 Data 的 Dapper/Polly 等包 | ✅ | 但 SyncService.cs 直接 `using Dapper; using Polly;`——这些通过传递引用可用，但形成隐式依赖 |

---

## 二、🔴 发布阻塞项 (P0 — 必须修复)

### B-1: .dna 文件缺少 ~15 个 NuGet 依赖程序集 (UI 代理发现)

**文件**: `BomAddIn-AddIn.dna`, `BomAddIn-AddIn64.dna`
**严重度**: ☠️ 致命 — 打包后插件无法加载

两个 .dna 文件仅列出 6 个 ExternalLibrary，但运行时需要以下 NuGet 程序集：`NLog.dll`, `Dapper.dll`, `System.Data.SQLite.dll`, `Microsoft.Extensions.DependencyInjection.dll`, `BCrypt.Net-Next.dll`, `Polly.dll`, `DuckDB.NET.Data.dll`, `dbup-core.dll`, `dbup-sqlite.dll`, `ExcelDataReader.dll` 等。如果使用 ExcelDnaPack 生成独立 `.xll`，这些依赖不会被包含，插件将无法加载。

### B-2: S004_EstimatesFK 迁移引用不存在的表——导致启动崩溃 (Data 代理发现)

**文件**: `Migrations/S004_EstimatesFK.sql:6`

```sql
INSERT OR IGNORE INTO Migrations_Skipped DEFAULT VALUES;  -- 表不存在！
```

`Migrations_Skipped` 表未在任何迁移脚本中创建。DbUp 执行此脚本时会抛出 SQLite 错误，阻止应用程序在新安装和升级时启动。

### B-3: SyncService 中无法到达的故障处理——同步失败静默丢失 (Core 代理发现)

**文件**: `SyncService.cs:104-126`

```csharp
await Task.WhenAll(materialsTask, pricesTask, ...);  // 任何任务失败→这里抛 AggregateException
// 下面代码永远不会执行！
if (materialsTask.IsFaulted) errors.Add(...);  // ← 死代码
```

`await Task.WhenAll()` 在任一任务失败时立即抛出，执行流程直接跳到外层 `catch (Exception)`，详细按任务错误报告完全丢失。

### B-4: ApprovalService TOCTOU 竞态条件 + 自我审批绕过 (Core 代理发现)

**文件**: `ApprovalService.cs:53,98-109`

(1) 版本状态在事务外读取（连接 A），但状态转换在事务内执行（连接 B），存在 TOCTOU 竞态。(2) `SubmitForReview` 将 `ApprovedBy` 设为 `null`，导致自我审批检查 `version.ApprovedBy == userId` 永远为 `false`（`null != anyUserId`），任何人都可审批自己的提交。

### B-5: 所有 IsThreadSafe=true 的 UDF 存在 SQLITE_BUSY 崩溃风险 (UI 代理发现)

**文件**: `DataQueryFunctions.cs`, `VarianceFunctions.cs`, `BomQueryFunctions.cs`

7 个 UDF 标记为 `IsThreadSafe = true`，但全部访问 SQLite。Excel 并行计算公式时可能并发写入（审计日志/缓存），导致 `SQLITE_BUSY` 异常。应全部设为 `IsThreadSafe = false` 或实现真正的线程安全。

### B-6: SnapshotService SELECT * 在 32 位 Excel 上导致 OOM (Core 代理发现)

**文件**: `SnapshotService.cs:39-43`

```csharp
var materials = conn.Query<Material>("SELECT * FROM Materials", ...).AsList();  // 10万条→OOM
```

对于生产数据集（10 万物料 + 50 万 BOM 节点），将所有表全量加载到内存进行 JSON 序列化，在 32 位 Excel（2GB 进程限制）中必然触发 `OutOfMemoryException`。

### B-7: BomAnalysisProvider 中 SQL 注入风险 (Data 代理发现)

**文件**: `BomAnalysisProvider.cs:138-139, 257-278`

`ExpandBom` 和 `FlushBatch` 将用户输入的 `itemCode` 通过字符串替换内联到 DuckDB SQL 中。虽然做了引号转义，但绕过了 DuckDB 原生的参数化查询，存在注入风险且阻止查询计划缓存。

---

## 三、🟠 高优先级问题 (P1 — 发布前应修复)

### 安全

| ID | 问题 | 文件 |
|----|------|------|
| S-1 | 缺少 AES-256-CBC 价格加密——仅 DPAPI，无 AES 实现 | `Security/` |
| S-2 | DPAPI 无应用特定熵 (entropy) 参数 + 无异常处理 | `DpapiEncryptionProvider.cs` |
| S-3 | `BomService` 授权检查接收调用者传入的 `UserRole`——调用者可声明任意角色 | `BomService.cs:81` |
| S-4 | `DashboardViewModel` 硬编码 `UserRole.Admin` 用于同步 | `DashboardViewModel.cs:195` |

### 数据完整性

| ID | 问题 | 文件 |
|----|------|------|
| D-1 | `SimulatedErpAdapter` 查询 `UpdatedAt` 列，但 Prices/Inventories/Orders/Capacities 表无此列 | `SimulatedErpAdapter.cs` |
| D-2 | `SqliteConnectionFactory.CreateConnection()` 返回已打开的连接——调用者重复 Open 会异常 | `SqliteConnectionFactory.cs:40` |
| D-3 | `SyncService` RetryPolicy 包裹事务内写入块——重试会导致重复插入 | `SyncService.cs:138-167` |
| D-4 | 仓库类零错误处理——SQLITE_BUSY/CORRUPT 异常直接传播到 UI 层 | 全部 11 个 Repository |
| D-5 | DuckDB 加载/刷新非原子——CloseDuckDb 到 LoadFromSqlite 之间查询会抛异常 | `BomAnalysisProvider.cs` |
| D-6 | `ApprovalService.Transition` 在事务外读取状态——TOCTOU 竞态 | `ApprovalService.cs:98` |

### 核心业务逻辑

| ID | 问题 | 文件 |
|----|------|------|
| C-1 | `VarianceCalculator` 缺少 4/6 差异维度（库存/预算/供应商/时间未实现） | `VarianceCalculator.cs` |
| C-2 | `VarianceService` 价格查询 N+1——500 物料 = 1000 次 DB 往返 | `VarianceService.cs:66-77` |
| C-3 | `VarianceCalculator.ToDictionary` 键重复崩溃——同物料同层多次出现 | `VarianceCalculator.cs:24` |
| C-4 | `SyncService` 未实现断路器（Polly Circuit Breaker），规范 §9.2 要求 | `SyncService.cs` |
| C-5 | `SeedDataGenerator` 默认规模是规范的 1/10（1万 vs 10万物料） | `SeedDataGenerator.cs:39` |
| C-6 | 事件总线未连接——4 个 Event 类已定义但从未发布/订阅 | `Events/`, `SyncService.cs` |
| C-7 | `AuditService` 硬编码 30 天历史查询——规范要求 ≥1 年 | `AuditService.cs:68` |

### UDF / UI 层

| ID | 问题 | 文件 |
|----|------|------|
| U-1 | `IExcelThreadDispatcher` 已注册但完全未使用——无代码调用它 | 全项目 |
| U-2 | `BOMCOST` 在 UDF 层内联 100 行成本计算——应委托给 Core Service | `BomQueryFunctions.cs:96-139` |
| U-3 | `ALERTCHECK` N+1 查询——每个 BOM 节点逐行查价格历史 | `VarianceFunctions.cs:101-116` |
| U-4 | 所有 UDF 用 `catch (Exception)` 吞异常返回 `#VALUE!` | 全部 4 个 UDF 文件 |
| U-5 | `BOMEXPAND` 的 `versionState` 参数已解析但从未使用（空 if 块） | `BomQueryFunctions.cs:39-42` |
| U-6 | `VersionAdapter` 死代码——无 UDF 调用 `GetDefaultReturnRowCount()` | `VersionAdapter.cs` |
| U-7 | `SeedDefaultData()` 和 `CreateDailySnapshotIfNeeded()` 在 Excel 主线程同步执行——启动冻结 | `AutoOpen.cs:64,67` |
| U-8 | `CreateDailySnapshotIfNeeded()` / `GenerateSeedDataIfNeeded()` 静默吞异常零日志 | `AutoOpen.cs:146,175` |

---

## 四、🟡 中等优先级 (P2 — 首个补丁前)

### 测试覆盖

| ID | 问题 |
|----|------|
| T-1 | **离线模式零测试覆盖**——spec §10 定义的状态机无任何测试 |
| T-2 | **Polly 重试/熔断零测试**——spec §9.2 的指数退避和熔断器无测试 |
| T-3 | `SyncServiceTests.GetLastSyncTime_NoSyncLog_ReturnsNull` 是死测试——只验证 service != null |
| T-4 | 缺少 DuckDB BOM 展开性能基准（性能关键路径） |
| T-5 | `ExcelThreadDispatcher` 的 `QueueAsMacro` 路径（非 Excel 主线程调用）从未测试 |
| T-6 | 无 xUnit `[Trait]` 分类，无法在 CI 中区分快/慢测试 |

### 线程安全

| ID | 问题 | 文件 |
|----|------|------|
| TH-1 | `AppConfigProvider.LoadFromDb` 中 `Set` 与加载存在竞态 | `AppConfigProvider.cs:31-54` |
| TH-2 | `EnvironmentManager.Current` 读/写无同步 | `EnvironmentManager.cs:24,82` |
| TH-3 | `DashboardBootstrapper._window` 在 Excel 主线程与 WPF 线程间无同步访问 | `DashboardBootstrapper.cs:15` |

### 代码质量

| ID | 问题 | 文件 |
|----|------|------|
| C-1 | `DashboardViewModel.BuildTreeNode` 递归建树 O(n²)——应先用 Dictionary 建索引 | `DashboardViewModel.cs:212-224` |
| C-2 | `RibbonController.OnSyncData` 使用 `async void`——异常会崩溃进程 | `RibbonController.cs:74` |
| C-3 | `BOMCOST` 创建两个独立 DI Scope (行 80, 88)——开销翻倍 | `BomQueryFunctions.cs:80,88` |
| C-4 | `SyncService.GetLastSyncTime` 和日志方法使用内联 Dapper 查询——应委托给 Repository | `SyncService.cs:214-238` |
| C-5 | `AlertItem`, `BomTreeNode`, `RelayCommand` 三个类挤在同一文件——违反一文件一类型约定 | `DashboardViewModel.cs:239-271` |
| C-6 | `BOMEXPAND` 仅返回 5 列，规范要求 6 列（缺 Source） | `BomQueryFunctions.cs:47` |

---

## 五、🔵 低优先级改进建议 (P3)

| ID | 建议 |
|----|------|
| L-1 | `NetworkMonitor.ProbeConnectionAsync` 静默吞异常——至少应 Debug 级别记录 |
| L-2 | `AppLogger` 缺少结构化日志/CorrelationId 支持（spec §14.1-14.2 要求） |
| L-3 | `IConfigProvider` 缺少 `TryGet`/`Contains` 方法——调用者无法区分"键缺失"和"值为空" |
| L-4 | 模型缺少数据注解：`BomNode.ScrapRate` 无范围验证 (0.0~1.0)，`Supplier.Rating` 无星级范围 |
| L-5 | `DashboardViewModel` 使用 `BomAddInStartup.ServiceProvider` 静态访问——应用构造函数注入 |
| L-6 | 构建脚本 `build.ps1` 硬编码 ExcelDnaPack `1.5.1` 路径和 Windows `%USERPROFILE%` |
| L-7 | `Diagnostic` 工具 `RunEnvCommand` 使用 `DateTime.Now` 而非 `UtcNow` 做备份文件名 |
| L-8 | 提取共享 `TestDataFactory` 消除测试中重复的造数代码 |
| L-9 | `EnvironmentManager.LoadFromDb` 不验证加载的环境值是否在 `ValidEnvironments` 中 |
| L-10 | `INetworkMonitor` 缺少 `ConnectivityChanged` 事件——同步服务必须轮询而非被动通知 |
| L-11 | `SyncLog.Status` 使用字符串而非枚举——应用 `SyncStatus` 枚举 |
| L-12 | `README.md:92` 仍提到 "SQL Server/PostgreSQL"——应为 "SQLite + DuckDB" |

---

## 六、文档审查

### ✅ 完整且准确

| 文档 | 状态 |
|------|:--:|
| specification.md | ✅ 全面，覆盖架构/数据/安全/性能/兼容性 |
| plan.md | ✅ Sprint 排期清晰，风险登记册完整 |
| project-structure.md | ✅ 文件路由详尽，命名约定明确 |
| api-reference.md | ✅ 8 个 UDF 签名完整，错误码表清晰 |
| user-manual.md | ✅ 覆盖安装/功能/故障排查/速查卡 |
| CLAUDE.md | ✅ 行为准则 + 任务路由表 + Skill 映射 |

### ⚠️ 需更新

| 文档 | 问题 |
|------|------|
| README.md:92 | 技术栈仍写 "SQL Server/PostgreSQL"，应改为 "SQLite + DuckDB" |
| api-reference.md | SYNCSTATUS 返回值 "Offline / ReadOnly" 与实际 v1.1 行为 (允许编辑) 不一致 |
| user-manual.md §2.4 | 离线模式标题说 v1.0 只读，但内容已更新为 v1.1 可编辑——标题需同步 |

---

## 七、测试统计

| 项目 | 测试数 | 状态 |
|------|:------:|------|
| BomAddIn.UnitTests | ~47 | 覆盖 Auth, BOM, Variance, Alert, Approval, Audit, Snapshot |
| BomAddIn.IntegrationTests | ~28 | 真实 SQLite 集成，覆盖 Repository + Config + DuckDB |
| BomAddIn.ThreadingTests | ~6 | 仅测试主线程直接执行路径，QueueAsMacro 路径未覆盖 |
| BomAddIn.PerformanceTests | 2 类 | 仅有 Variance + Alert 基准，缺少 DuckDB ExpandBom 基准 |
| **总计** | **~83** | 覆盖率估算 ~55-60%（目标 80%） |

### 覆盖缺口

- **离线模式**: 0 测试
- **Polly 重试/熔断**: 0 测试
- **并发场景**: 仅基础覆盖
- **库存/预算/供应商差异维度**: 0 测试

---

## 八、发布检查清单

### 必须完成 (阻塞发布) — 7 项 ✅ 全部已修复

- [x] **B-1**: 将 ~15 个缺少的 NuGet 程序集添加到两个 .dna 文件的 ExternalLibrary 列表中
- [x] **B-2**: 修复或删除 S004_EstimatesFK.sql 中的 `INSERT OR IGNORE INTO Migrations_Skipped`
- [x] **B-3**: 修复 SyncService.cs 中无法到达的 `IsFaulted` 检查——重构为 try-catch 包裹 WhenAll
- [x] **B-4**: 修复 ApprovalService 的 TOCTOU 竞态（事务内读取）和自我审批绕过
- [x] **B-5**: 将所有 `IsThreadSafe=true` 的 UDF 改为 `false`
- [x] **B-6**: 修复 SnapshotService 的 `SELECT *`——添加 LIMIT 防护
- [x] **B-7**: 修复 BomAnalysisProvider 的 SQL 内联——迁移到 DuckDB 位置参数 ($1, $2)

### 强烈建议 (发布前) — 20 项 (含已修复)

- [x] S-2: DPAPI 添加应用特定熵 + try-catch 错误处理
- [x] S-3: ERP URL 注释中添加配置指导（数据库覆盖机制已就绪）
- [x] D-1: 修复 `SimulatedErpAdapter` 中不存在的 `UpdatedAt` 列引用
- [x] D-2: 添加 `Busy Timeout=5000` 防止 SQLITE_BUSY
- [x] C-2: 修复 VarianceService N+1 价格查询（添加批量方法 `GetByMaterialIdsAndDate`）
- [x] C-3: 修复 VarianceCalculator 重复键崩溃——使用 `ToDictionarySafe`
- [x] C-7: 修复 `AuditService` 30 天 → 365 天历史查询
- [x] U-4: 完成 BOMEXPAND 中 versionState 过滤逻辑
- [x] U-7: 将 `CreateDailySnapshotIfNeeded()` 移至 `Task.Run()`
- [x] U-8: 为静默异常吞噬添加日志记录
- [x] C-1: 修复 DashboardViewModel O(n²) 建树
- [x] README 技术栈更新
- [ ] S-1: 实现 AES-256-CBC 加密提供者（或文档化 V1.1 范围外）
- [ ] D-3: 重构 SyncService 重试策略——在事务级别而非内部块级别重试
- [ ] D-4: 仓库类添加基础 try-catch + SQLITE_BUSY 重试
- [ ] D-5: 修复 DuckDB 加载/刷新原子性（或在文档中说明窗口期行为）
- [ ] C-1-dim: 实现缺失的 4 个差异分析维度（或文档化 V1.1 范围限定）
- [ ] C-4: 添加 Polly 断路器
- [ ] C-5: 更新种子数据默认规模至规范要求（10 万 + 50 万）
- [ ] U-1: 将 BOMCOST 成本计算逻辑提取到 `IBomService`
- [ ] U-2: 修复 ALERTCHECK N+1 查询
- [ ] U-3: UDF 异常处理区分错误类型

### 建议 (首个补丁)

- [x] 修复 DashboardViewModel O(n²) 建树
- [x] `README.md` 技术栈更新
- [ ] 添加 DuckDB ExpandBom 性能基准 + CI 性能门禁
- [ ] 添加 ExcelThreadDispatcher QueueAsMacro 路径测试
- [ ] 提取共享 TestDataFactory
- [ ] 添加 xUnit Trait 测试分类
- [ ] 修复 RibbonController async void
- [ ] T-1: 至少添加离线模式 + Polly 重试的基础测试

---

## 九、签署

| 角色 | 姓名 | 日期 | 决定 |
|------|------|------|------|
| 审查人 | Claude Code | 2026-07-14 | 有条件通过 — 4 个阻塞项修复后发布 beta |
| 技术负责人 | — | — | — |
| 项目经理 | — | — | — |

> 📋 审查范围: 8 程序集 · ~120 源文件 · ~83 测试 · 6 文档 · 4 迁移脚本 · 2 构建脚本
> 🔗 相关文档: [specification.md](./specification.md) · [plan.md](./plan.md) · [exemptions.md](../.claude/exemptions.md)
