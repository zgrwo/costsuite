# BomAddIn v1.1 全量深度审查报告

> **日期**: 2026-07-14
> **审查类型**: 全量深度审查（源码 · 测试 · 文档）
> **审查方法**: 5 个并行专业代理，覆盖全部 5 层
> **审查范围**: ~120 源文件 + ~70 测试 + 17 份文档

---

## 执行摘要

本次全量深度审查在 v1.1 发布前全面审查之后进行，聚焦于更深层的问题：算法正确性、数值精度、边界处理、SQL 正确性、并发安全、测试质量和文档一致性。

**总计发现**: **52 个严重问题** · **110 个中等问题** · **71 个轻微问题**

| 审查维度 | 严重 | 中等 | 轻微 | 核心风险 |
|---------|:----:|:----:|:----:|---------|
| Core 服务层 | 23 | 20 | 7 | BOM 成本重复计数、版本匹配键错误、自审批绕过、JSON 解析脆弱 |
| Data/Infra 层 | 5 | 22 | 13 | 枚举序列化 Bug、DEK 覆盖数据丢失、CASCADE DELETE、SQL 注入设计 |
| UI/Bridge/UDF 层 | 7 | 21 | 19 | 跨线程绑定崩溃、COM 非主线程调用、UI 同步阻塞 |
| 测试 | 16 | 27 | 16 | SyncService 零在线测试、静态共享状态、大量弱断言 |
| 文档 | 1 | 10 | 16 | SYNCSTATUS 返回值完全错误、离线术语陈旧、多处不一致 |

---

## 一、Core 服务层 — 最严重发现

### 🔴 C-1: BomService.CalculateCost — 同物料多出现成本覆盖

**文件**: `src/BomAddIn.Core/Services/BomService.cs:216, 227-228`

`costs` 字典以 `MaterialId` 为键。同一物料在 BOM 中多处出现时（如螺丝 MAT-000042 在子装配 A Level=2 和子装配 B Level=3），后一个节点的成本覆盖前一个。父节点通过 `costs.TryGetValue(child.MaterialId, ...)` 汇总子节点成本时只能取到一个值。

**影响**: 任何非平凡 BOM 的成本计算都会产生系统性偏差。

**修复**: 使用节点唯一标识符作为字典键，或直接遍历所有子节点累加（不用字典查找）。

### 🔴 C-2: BomService.CalculateCost — 回退路径重复计数

**文件**: `src/BomAddIn.Core/Services/BomService.cs:240-243`

当没有 Level=0 根节点时，回退分支对 `costs[n.MaterialId]` 求和。但 `costs[node]` 已经包含自身成本 + 所有子节点成本，求和导致父节点成本被重复计算。

**修复**: 回退路径只累加 `ownCost`，或使用不同的累加策略。

### 🔴 C-3: VarianceCalculator.ToDictionarySafe — 跨版本匹配键错误

**文件**: `src/BomAddIn.Core/Services/VarianceCalculator.cs:99-116`

`ToDictionarySafe` 在两个版本上独立调用。当版本 A 有 2 个相同键的节点、版本 B 有 1 个时：
- 版本A: `"MAT-001|100|2"` 和 `"MAT-001|100|2#2"`
- 版本B: `"MAT-001|100|2"`
- `"MAT-001|100|2#2"` 在 B 中不存在 → 被报告为 **Removed**（错误！这是有效重复节点）

**修复**: 使用分组列表比较替代字典查找，或在分组内保证确定性排序。

### 🔴 C-4: ApprovalService — 自审批检查用错字段

**文件**: `src/BomAddIn.Core/Services/ApprovalService.cs:53-57`

```csharp
if (version.ApprovedBy.HasValue && version.ApprovedBy.Value == userId ...)
```

`ApprovedBy` 是审批人。`PendingReview` 状态的版本尚未被审批，`ApprovedBy` 应为 null。正确的检查应该是 `SubmittedBy == userId`（提交人不能审批自己的提交）。当前逻辑永远无法阻止自审批。

### 🔴 C-5: ApprovalService — TOCTOU 绕过自审批检查

**文件**: `src/BomAddIn.Core/Services/ApprovalService.cs:51-60`

`Approve()` 在事务外读取版本做自审批检查（第51行），然后在事务内再次读取版本做状态转换（第60行 → 第107行）。两读之间有窗口，另一个线程可修改版本状态。

**修复**: 将自审批检查移入 `Transition()` 的事务内。

### 🔴 C-6: AuditService.ToJson — 无限递归风险

**文件**: `src/BomAddIn.Core/Services/AuditService.cs:85-124`

如果 POCO 有循环引用属性（ORM 延迟加载代理或领域对象双向引用），`ToJson` 无限递归导致 `StackOverflowException`。

**修复**: 添加 `HashSet<object>` 跟踪已访问对象，或限制递归深度。

### 🔴 C-7: SnapshotService.ParseSnapshotTables — 手写 JSON 解析器脆弱

**文件**: `src/BomAddIn.Core/Services/SnapshotService.cs:193-226`

用字符串操作逐行解析 JSON：检测表头用 `StartsWith("\"") && EndsWith("\": {")`，计数用 `StartsWith("\"") && Contains(":")`。任何合法 JSON 格式变化（不同缩进、压缩 JSON、字符串值含冒号）都会导致错误行数。

**修复**: 使用 `System.Text.Json` 或 `Newtonsoft.Json` 进行正式解析。

### 🔴 C-8: SnapshotService — LIMIT 50000 静默截断

**文件**: `src/BomAddIn.Core/Services/SnapshotService.cs:40-50`

表超过 50,000 行时静默截断，无警告、无错误、无标记。后续 `Compare()` 产生误导结果（显示"删除"了从未捕获的行）。

**修复**: 比较 `rows.Count` 与 `maxRowsPerTable`，记录警告，返回 `IsTruncated` 标志。

### 🔴 C-9: SyncService — Thread-pool 线程浪费

**文件**: `src/BomAddIn.Core/Services/SyncService.cs:66-71`

```csharp
await CircuitBreaker.ExecuteAsync(() => RetryPolicy.ExecuteAsync(() => Task.Run(transactionBody)));
```

同步 `Action` 用 `Task.Run` 包装——对于处理数千行的 SyncAll，线程被阻塞在 SQLite I/O 上。

**修复**: Repository 方法暴露 async 重载，或使用同步 Polly 策略。

### 🔴 C-10: BomExcelImporter — 先插入后检测循环

**文件**: `src/BomAddIn.Core/Services/BomExcelImporter.cs:240-253`

节点在第 231 行插入，循环引用在第 243 行检测。有循环时整个事务回滚——所有 INSERT 工作浪费。

**修复**: 先对边列表做循环检测，确认无循环后再批量插入。

### 🔴 C-11: BomExcelImporter.DfsAll — 深度 BOM 栈溢出

**文件**: `src/BomAddIn.Core/Services/BomExcelImporter.cs:319-352`

`DfsAll` 完全递归。线性链 10,000 层会栈溢出。

**修复**: 改为显式 `Stack<>` 迭代 DFS，或添加深度守卫。

### 🔴 C-12: VarianceCalculator.ComparePrices — 零价格百分比错误

**文件**: `src/BomAddIn.Core/Services/VarianceCalculator.cs:148`

```csharp
var changePct = priceA > 0 ? (priceB - priceA) / priceA * 100 : 0;
```

`priceA == 0, priceB == 100`（新品定价）→ 返回 0% 而非无穷大。与 `CompareBomVersions` 第 67 行（正确处理为 `double.PositiveInfinity`）不一致。

**修复**: 与数量变化一致处理。

### 🔴 C-13: AlertEvaluator — 阈值未验证

**文件**: `src/BomAddIn.Core/Services/AlertEvaluator.cs:23-29`

构造函数不验证阈值顺序。配置 `Critical=50, Severe=60, Warning=80` 时，Critical 规则永远不可达（Severe 先匹配）。

**修复**: 构造函数添加验证：`Critical > Severe > Warning`。

### 🔴 C-14: AesEncryptionProvider — DEK 损坏时静默覆盖

**文件**: `src/BomAddIn.Infrastructure/Security/AesEncryptionProvider.cs:131-165`

DPAPI 解密失败（密码重置、域变更、配置迁移）→ 生成新密钥 → **直接覆盖旧 DEK 文件** → 所有用旧密钥加密的历史数据永久不可恢复。

**修复**: 备份旧 DEK 文件（加时间戳），生成新密钥写新文件，记录 Fatal 日志。

---

## 二、Data/Infrastructure 层 — 最严重发现

### 🔴 C-15: BomNodeRepository.Update — 枚举序列化为数字

**文件**: `src/BomAddIn.Data/Repositories/BomNodeRepository.cs:96-115`

`Update()` 直接传 `node` 对象给 Dapper，`VersionState` 枚举被序列化为整数 `0/1/2`。但 `Add()` 正确使用了 `GetBomNodeParams()` 转为字符串 `"Draft"/"Released"`。

**后果**: Update 操作将 `VersionState` 从 `"Released"` 改为 `"0"` → 后续 `WHERE VersionState = 'Released'` 查不到这些行。

### 🔴 C-16: ON DELETE CASCADE — 连锁删除风险

**文件**: `src/BomAddIn.Data/Migrations/S001_InitialSchema.sql:29,30,62,63,76,89,195`

删除一个物料会级联删除：所有 BOM 结构 + 价格记录 + 库存记录 + 订单记录 + 版本记录。对一个 BOM 管理系统，这极其危险。

**修复**: 全部改为 `ON DELETE RESTRICT`。

### 🔴 C-17: DuckDB FlushBatch — SQL 注入设计缺陷

**文件**: `src/BomAddIn.Data/Analysis/BomAnalysisProvider.cs:268-295`

字符串拼接构建 INSERT SQL。虽然注解标明"数据来自本地 SQLite（可信源）"，但 `val.ToString()` 回退路径直接拼接未转义的值——如果类型不是预期的 `string/DateTime/bool`，会产生 SQL 注入。

**修复**: 使用 DuckDB Appender API 替代字符串拼接。

### 🔴 C-18: 枚举类型映射缺失

**文件**: 跨 `UserRepository.cs`, `BomNodeRepository.cs`, `BomVersionRepository.cs`

Dapper 从 TEXT 列读取 `"Admin"` 时无法映射到 `UserRole` 枚举（默认按整数值处理）。如果未注册 `SqlMapper.AddTypeHandler`，查询会抛出 `InvalidCastException`。

### 🔴 C-19: CTE 深度 20 层静默截断

**文件**: `src/BomAddIn.Data/Analysis/BomAnalysisProvider.cs:130`

`WHERE bt.Level < 20` 硬编码。BOM 超过 20 层时静默截断，无警告。

---

## 三、UI/Bridge/UDF 层 — 最严重发现

### 🔴 C-20: DashboardViewModel — 后台线程更新绑定属性

**文件**: `src/BomAddIn/Dashboard/DashboardViewModel.cs:47-51`

`Task.Run(() => RefreshAll())` 在线程池执行，内部设置 `StatusText`、`MaterialCount` 等绑定属性。`PropertyChanged` 在非 UI 线程触发 → WPF 绑定跨线程异常 `InvalidOperationException`。

### 🔴 C-21: VersionAdapter — 非主线程 COM 调用

**文件**: `src/BomAddIn/Bridge/VersionAdapter.cs:40-41`

`Lazy<bool>` 的延迟求值意味着 `dynamic` COM 调用可能在任何首次访问 `IsDynamicArraySupported` 的线程触发。非 Excel 主线程 → `RPC_E_WRONG_THREAD`。

**修复**: 在 `AutoOpen` 中强制求值 `Lazy<bool>`，或注入 `IExcelThreadDispatcher`。

### 🔴 C-22: DashboardViewModel — UI 线程同步阻塞数据库查询

**文件**: `src/BomAddIn/Dashboard/DashboardViewModel.cs:153-185`

`ExpandBom` 通过 `ICommand` 在 WPF 线程同步执行 `bomService.Expand()`——大 BOM（数万节点）会长时间卡死 UI。

### 🔴 C-23: BomTaskPane — 构造函数中同步访问数据库

**文件**: `src/BomAddIn/UI/TaskPane/BomTaskPane.xaml.cs:29`

构造函数调用 `RefreshSyncStatus()` → 同步 SQLite 查询 → Excel 主线程阻塞。

### 🔴 C-24: AutoOpen — 后台 Task 与 ServiceProvider.Dispose 竞态

**文件**: `src/BomAddIn/Bootstrap/AutoOpen.cs:67-75, 98-106`

3 个 fire-and-forget `Task.Run` 在 `AutoClose` dispose `ServiceProvider` 时可能仍在运行 → `CreateScope()` 抛出 `ObjectDisposedException`。

---

## 四、测试 — 最严重发现

### 🔴 C-25: SyncService — 在线路径零测试

**文件**: `tests/BomAddIn.UnitTests/SyncServiceTests.cs`

仅 3 个测试，全部覆盖离线/跳过路径。ERP 同步的核心在线路径（物料/价格/库存/订单/产能同步、部分失败、增量同步、重复同步防护）**零覆盖**。

### 🔴 C-26: ExcelThreadDispatcher — 跨线程路径零测试

**文件**: `tests/BomAddIn.ThreadingTests/ThreadSafetyTests.cs:110-128`

两个测试仅在主线程运行（测试 trivial 路径）。**关键路径**（从后台线程 marshaling 到 Excel 主线程）完全未测试——这正是生产环境中 `RPC_E_WRONG_THREAD` 的来源。

### 🔴 C-27: BomService — 核心操作测试严重不足

**文件**: `tests/BomAddIn.UnitTests/BomServiceTests.cs`

8 个测试，缺失：null/空物料编码 Expand、不存在物料 Expand、5+ 层级 BOM Expand、循环引用处理、缓存线程安全、UpdateNode/DeleteNode 不存在 ID。

### 🔴 C-28: 静态共享可变状态

**文件**: `tests/BomAddIn.IntegrationTests/MaterialRepositoryTests.cs:13` 和 `PriceRecordRepositoryTests.cs:14`

```csharp
private static int _counter;
```

跨测试类共享的 `static` 字段，xUnit 并行运行时会竞态。

### 🔴 C-29: AuditService.ToJson — 测试名与实现矛盾

`ToJson_String_ShouldReturnQuotedString` 断言 `"hello"` 但不含 JSON 引号。`ToJson(null)` 返回 C# 字符串 `"null"` 而非 JSON `null`。

### 🔴 C-30: 广泛弱断言

大量测试仅断言 `NotNull`、`Count > 0`、`Id > 0`，不验证实际值。例如 `GetAll_ReturnsAllConfig` 仅断言 `NotNull`，不检查返回的键值对是否正确。

### 🔴 C-31: 8 个 Repository 零测试

`UserRepository`, `UserTokenRepository`, `BomVersionRepository`, `InventoryRecordRepository`, `OrderRecordRepository`, `CapacityRecordRepository`, `SyncLogRepository`, `DataSnapshotRepository`, `SupplierRepository` — 零集成测试。

### 🔴 C-32: UDF 函数零测试

全部 8 个 Excel UDF 函数无任何测试（单元或集成）。

---

## 五、文档 — 最严重发现

### 🔴 C-33: api-reference.md — SYNCSTATUS 返回值完全错误

**文件**: `docs/api-reference.md:294-307`

文档列出的返回值（"Online / Synced"、"Offline / ReadOnly"）与代码实际输出（"Never synced"、"Synced Xm ago"）完全无关。

### 🟠 文档中等 — 主要问题

- "离线仅限只读"在 4 处文档中仍存在，与 v1.1 可编辑矛盾
- DI 注册名称在 spec / skill / 实际代码间不一致
- ThreadDispatcher 设计在 skill（静态类）与 spec（DI 接口）间不一致
- 发布前审查有 10+ 项未解决发现，无跟踪
- 迁移脚本在 project-structure.md 中被列出两次，路径不一致
- plan.md 引用 "SQL Server/PostgreSQL" 但项目使用 SQLite+DuckDB
- 离线优先架构 skill 描述的 ConnectionStateMachine 与代码实现差距极大

---

## 六、汇总与修复优先级

### P0 — 立即修复（阻塞 v1.1）

| # | 问题 | 文件 | 影响 |
|---|------|------|------|
| C-1 | BOM 成本重复计数 | BomService.cs | 所有非平凡 BOM 成本错误 |
| C-3 | 跨版本匹配键错误 | VarianceCalculator.cs | 差异分析假阳性 |
| C-15 | 枚举序列化为数字 | BomNodeRepository.cs | VersionState 列数据损坏 |
| C-14 | DEK 覆盖致数据丢失 | AesEncryptionProvider.cs | 历史加密数据永久丢失 |
| C-4 | 自审批检查用错字段 | ApprovalService.cs | 安全控制完全绕过 |
| C-20 | 后台线程更新绑定属性 | DashboardViewModel.cs | 运行时崩溃 |
| C-33 | SYNCSTATUS 文档与代码不符 | api-reference.md | 用户误导 |

### P1 — 发布前修复

| # | 问题 | 文件 |
|---|------|------|
| C-16 | ON DELETE CASCADE | S001_InitialSchema.sql |
| C-17 | DuckDB SQL 注入设计 | BomAnalysisProvider.cs |
| C-18 | 枚举类型映射缺失 | 多个 Repository |
| C-7 | 手写 JSON 解析器 | SnapshotService.cs |
| C-5 | TOCTOU 自审批绕过 | ApprovalService.cs |
| C-21 | 非主线程 COM 调用 | VersionAdapter.cs |
| C-24 | 后台 Task 竞态 | AutoOpen.cs |
| C-10 | 先插入后检测循环 | BomExcelImporter.cs |
| C-8 | LIMIT 50000 静默截断 | SnapshotService.cs |
| C-19 | CTE 20 层静默截断 | BomAnalysisProvider.cs |

### P2 — v1.1 patch 修复

| # | 问题 |
|---|------|
| C-25 | SyncService 在线路径零测试 |
| C-26 | ExcelThreadDispatcher 跨线程零测试 |
| C-27 | BomService 核心操作测试不足 |
| C-2 | BOM 成本回退路径双计 |
| C-9 | Thread-pool 线程浪费 |
| C-11 | 深度 BOM 栈溢出 |
| C-12 | 零价格百分比错误 |
| C-13 | 阈值未验证 |
| C-22 | Dashboard UI 同步阻塞 |
| C-23 | TaskPane 构造同步 DB |

---

## 七、与上次审查的对比

上次发布前审查（pre-release-review-2026-07-14.md）发现了 77 项发现，其中 7 个阻塞项。本次深度审查聚焦于更隐蔽的问题：

| 类别 | 上次审查 | 本次新增 |
|------|---------|---------|
| 算法/数值错误 | 3 项 | **12 项** (C-1,C-2,C-3,C-5,C-6,C-12 等) |
| 数据持久化 Bug | 1 项 | **5 项** (C-15,C-14,C-16,C-18,C-8) |
| 并发/线程安全 | 4 项 | **5 项** (C-20,C-21,C-24,C-22,C-23) |
| 测试质量 | 5 项 | **8 项** (C-25~C-32) |
| 文档一致性 | 4 项 | **1 严重 + 10 中等** |

**关键差异**: 上次审查聚焦于"缺什么"（缺失实现、缺失测试、缺失错误处理），本次审查发现了大量"写错了"（算法错误、序列化 Bug、字段用错、文档与代码完全不符）。

---

## 八、整体评价

项目架构扎实，代码组织良好，文档体系完整。但深度审查揭示了若干**隐蔽但高影响**的缺陷：BOM 成本计算错误、版本差异匹配错误、数据序列化 Bug、加密密钥管理缺陷等。这些问题的共同特征是：**常规测试不会捕获**（需要特定 BOM 结构、多版本差异、异常恢复路径）。

**建议**: 
1. 优先修复 P0 和 P1 项
2. 为每个 P0 修复添加专门的回归测试
3. 补充 SyncService、ExcelThreadDispatcher 和 UDF 的测试覆盖
4. 建立 BOM 成本计算和差异分析的正确性基准测试（已知 BOM 输入 → 已知结果）
5. 统一文档术语（离线模式、线程模型、DI 组件名）

---

> **审查耗时**: ~15 分钟并行（5 代理 × ~80 工具调用）
> **审查者**: Claude Code (DeepSeek v4 Pro)
> **审查模型**: 多代理分层并行审查
