# 企业级 BOM 管理与差异分析 Excel 插件 — 技术规格说明书

> **文档类型**: 技术规格说明书（Specification）  
> **日期**: 2026-07-12  
> **受众**: 开发者、架构师  
> **单源真理**: 架构设计、数据模型、安全策略、性能 KPI 以本文档为准  
> **配套文档**: [refactoring-plan.md](./refactoring-plan.md)（重构计划）、[api-reference.md](./api-reference.md)（UDF 权威合约）、[project-structure.md](./project-structure.md)（文件路由）
> **v1.1 变更**: 数据存储方案由 SQL Server/PostgreSQL → SQLite (CRUD) + DuckDB (分析)。

---

## 1. 系统概述

### 1.1 系统上下文

```text
┌──────────────────────────────────────────────────────────────────┐
│                        Microsoft Excel 宿主                       │
│  ┌─────────┐  ┌──────────┐  ┌──────────┐  ┌───────────────────┐  │
│  │ Ribbon  │  │ TaskPane │  │   UDFs   │  │  WPF Dashboard    │  │
│  │ (菜单)  │  │ (侧边栏) │  │ (公式栏) │  │  (独立STA窗口)    │  │
│  └────┬────┘  └────┬─────┘  └────┬─────┘  └────────┬──────────┘  │
│       └────────────┴─────────────┴─────────────────┘              │
│                          │ Excel-DNA 桥接层                        │
└──────────────────────────┼───────────────────────────────────────┘
                           │
          ┌────────────────┼────────────────┐
          ▼                ▼                 ▼
   ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
   │  SQLite      │ │  DuckDB      │ │  ERP System  │
   │  (CRUD 主库) │ │  (分析引擎)  │ │  (REST/模拟) │
   └──────────────┘ └──────────────┘ └──────────────┘
```

### 1.2 V1.0 核心约束

| # | 约束 | 影响范围 |
|---|------|---------|
| 1 | **离线模式（ERP 同步暂停）** — V1.0 不支持离线编辑与双向同步 → **v1.1 放宽**: SQLite 本地主库下编辑始终可用，离线仅指 ERP 同步暂停 | 同步服务、离线模式、UI 设计 |
| 2 | **Excel 2019+ 为基准** — 365 动态数组作为增强 | UDF 设计、差异分析输出格式 |
| 3 | **线程隔离强制** — 所有 COM 调用须经 `ExcelThreadDispatcher` | 桥接层、WPF 仪表盘、所有 Excel 交互 |

---

## 2. 架构设计

### 2.1 六层架构

```text
┌─────────────────────────────────────────────────────────────────────┐
│                        表示层 (UI Layer)                             │
│  Ribbon · TaskPane · WinForm · WPF 仪表盘（独立 STA 线程）           │
│  技术: WPF · MVVM · LiveCharts2/OxyPlot · WinForms                  │
├─────────────────────────────────────────────────────────────────────┤
│              Excel-DNA 桥接层 (Bridge Layer) 【V1.0 关键】           │
│  ExcelThreadDispatcher · AsyncUtil · COM Marshalling · VersionAdapt │
│  技术: Excel-DNA · ExcelAsyncUtil · COM Interop                     │
├─────────────────────────────────────────────────────────────────────┤
│                      业务逻辑层 (BLL)                                │
│  AuthService · SyncService · BomService · VarianceService           │
│  技术: C# · 手写验证 · 事件总线（进程内 pub-sub）              │
├─────────────────────────────────────────────────────────────────────┤
│                  差异计算引擎 (Variance Engine)                      │
│  多维比对 · 预警规则 · 快照回溯 · 审批状态                           │
│  技术: LINQ · 并行计算（PLINQ/Task Parallel）· 规则引擎              │
├─────────────────────────────────────────────────────────────────────┤
│                      数据访问层 (DAL)                                │
│  SQLite(CRUD) · DuckDB(分析) · MemoryCache(L1) · 简单重试        │
│  技术: Dapper · System.Data.SQLite · DuckDB.NET · DbUp             │
├─────────────────────────────────────────────────────────────────────┤
│                      基础设施层 (Infrastructure)                     │
│  文件日志 · CI/CD · 配置中心 · 审计日志 · BCrypt 密码哈希       │
│  技术: AppLogger · Microsoft.Extensions.DependencyInjection · BCrypt  │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 各层职责与接口

| 层 | 职责 | 公开接口（示例） | 依赖方向 | 线程模型 |
|----|------|-----------------|----------|----------|
| **UI** | 用户交互、数据展示 | Ribbon 命令、WPF ViewModel | → Bridge | STA（WPF 独立线程） |
| **Bridge** | Excel COM 封送、版本适配 | `IExcelThreadDispatcher`, `IVersionAdapter` | → BLL（仅数据对象） | Excel 主 STA |
| **BLL** | 业务规则、流程编排 | `IBomService`, `IVarianceService`, `IAuthService` | → Variance Engine, DAL | 线程安全（无状态） |
| **Variance Engine** | 差异计算、预警评估 | `IVarianceCalculator`, `IAlertEvaluator` | → DAL（只读） | 线程安全（纯计算） |
| **DAL** | 数据持久化、缓存 | `IMaterialRepository`, `IBomNodeRepository`, `IBomAnalysisProvider` | → Infrastructure | 线程安全（SQLite 连接池 + DuckDB 内存模式） |
| **Infrastructure** | 横切关注点（日志、安全、DI） | `AppLogger`, `IAppConfigProvider`, `IAuditService` | 所有层依赖 | 线程安全 |

### 2.3 依赖注入设计（Sprint 0 骨架）

> 📌 **原文档缺失**。参考 [Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)，在 `AutoOpen()` 中初始化 DI 容器。

```csharp
// 启动引导 (AutoOpen)
public static void AutoOpen()
{
    var services = new ServiceCollection();
    
    // 基础设施 — 参考 ServiceConfigurator.Configure() 查看完整注册
    services.AddSingleton<IAppConfigProvider, AppConfigProvider>();
    services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
    
    // 桥接层
    services.AddSingleton<IExcelThreadDispatcher, ExcelThreadDispatcher>();
    services.AddSingleton<IVersionAdapter, VersionAdapter>();
    
    // DAL
    services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
    services.AddScoped<IMaterialRepository, MaterialRepository>();
    services.AddScoped<IBomNodeRepository, BomNodeRepository>();
    
    // BLL
    services.AddScoped<IBomService, BomService>();
    services.AddScoped<IVarianceService, VarianceService>();
    services.AddScoped<ISyncService, SyncService>();
    
    _serviceProvider = services.BuildServiceProvider();
}
```

### 2.4 事件总线（推荐）

> 📌 **原文档缺失**。WPF Dashboard、UDF 计算、Ribbon 命令、同步服务之间的数据变更通知需要轻量 pub-sub。

- **实现选择**: `System.Reactive` Subject 或自定义 `IEventBus` 接口
- **事件类型**: `DataRefreshedEvent`, `SyncCompletedEvent`, `OfflineModeChangedEvent`
- **生产者**: SyncService（同步完成时）、AuthService（登录/登出时）
- **消费者**: WPF Dashboard（刷新图表）、TaskPane（更新状态栏）

---

## 3. 桥接层规格（关键基础设施）

### 3.1 IExcelThreadDispatcher

```csharp
/// <summary>
/// Excel 主线程调度器 — 解决 RPC_E_WRONG_THREAD 异常。
/// 所有 WPF 线程对 Excel COM 的调用必须通过此接口封送。
/// </summary>
public interface IExcelThreadDispatcher
{
    /// <summary>同步封送。调用方不在 Excel 主线程时通过 QueueAsMacro 执行</summary>
    T RunOnExcelThread<T>(Func<T> action);
    
    /// <summary>异步封送。不阻塞 WPF UI</summary>
    Task<T> RunOnExcelThreadAsync<T>(Func<T> action);
    
    /// <summary>当前是否在 Excel 主线程上</summary>
    bool IsExcelMainThread { get; }
}
```

```csharp
public class ExcelThreadDispatcher : IExcelThreadDispatcher
{
    public bool IsExcelMainThread =>
        System.Threading.Thread.CurrentThread.ManagedThreadId == _excelThreadId;
    
    private static int _excelThreadId; // AutoOpen 时捕获

    public static void Initialize()
    {
        _excelThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
    }

    public T RunOnExcelThread<T>(Func<T> action)
    {
        // 关键修复（原文档缺失）：已在主线程则直接执行，避免死锁
        if (IsExcelMainThread)
            return action();
        
        return ExcelAsyncUtil.QueueAsMacro(() => action());
    }

    public Task<T> RunOnExcelThreadAsync<T>(Func<T> action)
    {
        if (IsExcelMainThread)
            return Task.FromResult(action());
        
        var tcs = new TaskCompletionSource<T>();
        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
```

### 3.2 线程模型图

```text
┌──────────────────────┐     ┌──────────────────────┐
│  Excel 主线程 (STA)   │     │  WPF 仪表盘线程 (STA) │
│                      │     │                      │
│  • UDF 计算          │     │  • WPF Application   │
│  • Ribbon 事件       │◄───►│  • MVVM 绑定         │
│  • COM 对象操作      │Queue│  • 图表渲染          │
│  • 任务窗格          │AsMacro                     │
└─────────┬────────────┘     └──────────┬───────────┘
          │                             │
          │         ┌───────────────────┘
          │         ▼
          │  ┌──────────────────────┐
          │  │  后台线程池 (MTA)    │
          │  │                      │
          │  │  • 数据同步          │
          │  │  • 数据库查询        │
          │  │  • 差异计算          │
          │  └──────────────────────┘
          │
          ▼
   ┌──────────────────────┐
   │  定时器线程           │
   │  • 网络健康检查       │
   │  • 定时同步           │
   └──────────────────────┘
```

### 3.3 COM 对象生命周期规则

| 规则 | 说明 |
|------|------|
| **不要手动 `Marshal.ReleaseComObject`** | 在 Excel-DNA 插件中让 GC 处理清理工作 |
| **仅通过 `ExcelThreadDispatcher` 访问 COM** | 绝不从后台线程或 WPF 线程直接调用 `Globals.ThisAddIn.Application` |
| **使用 `ExcelReference` 替代 COM Range** | `ExcelReference` 是线程安全的轻量句柄，可跨线程传递 |
| **避免在 UDF 中缓存 COM 对象** | COM 引用可能在 Excel 会话期间失效 |

### 3.4 IVersionAdapter — Excel 版本兼容

```csharp
public interface IVersionAdapter
{
    bool IsDynamicArraySupported { get; }  // Excel 365+
    string GetArrayFormulaBehavior();       // "Ctrl+Shift+Enter" vs "动态溢出"
    int GetDefaultReturnRowCount();         // 2019 需固定区域大小
}
```

---

## 4. 数据模型

### 4.1 业务表（8 张）

| 表名 | 分类 | 核心字段 | 索引策略 |
|------|------|---------|----------|
| **Materials** | Owned | `Id`, `OrgId`, `Code`, `Name`, `Spec`, `Unit`, `Category`, `IsActive` | `OrgId+Code` 唯一索引 |
| **BomStructures** | Owned | `Id`, `OrgId`, `ParentMaterialId`, `ChildMaterialId`, `Quantity`, `Level`, `ValidFrom`, `ValidTo`, `VersionState` | `ParentMaterialId+ValidFrom` 复合索引 |
| **Suppliers** | Owned | `Id`, `OrgId`, `Code`, `Name`, `Contact`, `Rating` | `OrgId+Code` 唯一索引 |
| **Prices** | Synced-RO | `Id`, `OrgId`, `MaterialId`, `SupplierId`, `UnitPrice`, `Currency`, `DataVersion`, `EffectiveDate` | `MaterialId+DataVersion` |
| **Inventories** | Synced-RO | `Id`, `OrgId`, `MaterialId`, `WarehouseId`, `Quantity`, `DataVersion`, `SnapshotDate` | `MaterialId+WarehouseId+DataVersion` |
| **Orders** | Synced-RO | `Id`, `OrgId`, `MaterialId`, `OrderQty`, `DueDate`, `DataVersion` | `MaterialId+DueDate` |
| **Capacities** | Synced-RO | `Id`, `OrgId`, `WorkCenterId`, `CapacityHours`, `DataVersion` | `WorkCenterId+DataVersion` |
| **Estimates** | Owned | `Id`, `OrgId`, `BomVersionId`, `TotalCost`, `LaborHours`, `Notes` | `BomVersionId` |

**表分类**:
- **Owned**: 插件拥有写入权
- **Synced-RO**: 从 ERP 同步的只读缓存

### 4.2 系统表（7 张）

| 表名 | 用途 | 核心字段 |
|------|------|---------|
| **Users** | 用户认证 | `Id`, `Username`, `PasswordHash`(BCrypt), `Role`, `OrgId`, `IsActive` |
| **UserTokens** | Session/JWT | `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt` |
| **AuditLogs** | 操作审计 | `Id`, `UserId`, `Action`, `TableName`, `RecordId`, `OldValues`(JSON), `NewValues`(JSON), `Timestamp` |
| **SyncLogs** | 同步记录 | `Id`, `SyncType`, `StartedAt`, `CompletedAt`, `RecordsProcessed`, `Status`, `ErrorMessage` |
| **AppConfig** | 键值配置 | `Id`, `Key`, `Value`, `Description`, `UpdatedAt` |
| **DataSnapshots** | 数据快照 | `Id`, `SnapshotType`(Daily/Manual), `SnapshotData`(JSON/Binary), `CreatedAt`, `Description` |
| **BomVersions** | BOM 版本 | `Id`, `BomId`, `VersionNumber`, `State`(Draft/Released/Obsolete), `ApprovedBy`, `ApprovedAt` |

### 4.3 查询路径与引擎分工

| 场景 | 引擎 | 原因 | 性能目标 |
|------|------|------|---------|
| 物料/供应商 CRUD | SQLite (Dapper) | 单表主键查询，SQLite 最自然 | <10ms |
| BOM 展开（5 级递归） | DuckDB | 列式引擎 + 向量化，递归 CTE 原生支持 | <500ms (1000 节点) |
| 差异分析（两个 BOM 版本） | DuckDB | 大结果集 diff 在列式引擎上远快于 LINQ 内存对比 | <3s (10 万节点) |
| 价格趋势查询 | DuckDB | 时序聚合天然优势 | <100ms |
| 仪表盘 Dashboard 聚合 | DuckDB | SUM/AVG/窗口函数在列式引擎上亚秒级 | <1s |
| 配置/用户/Session | SQLite (Dapper) | 小表、低频率 | <10ms |

### 4.4 实体关系图 (ER Diagram)

#### 4.4.1 核心业务表

```text
┌──────────────────────┐        ┌──────────────────────┐
│      Suppliers       │        │      Materials       │
│──────────────────────│        │──────────────────────│
│ Id            PK     │        │ Id            PK     │
│ OrgId                │        │ OrgId                │
│ Code           UQ    │        │ Code           UQ    │
│ Name                 │        │ Name                 │
│ Contact              │        │ Spec                 │
│ Rating               │        │ Unit                 │
│ CreatedAt            │        │ Category             │
│ UpdatedAt            │        │ IsActive             │
└──────────┬───────────┘        │ CreatedAt            │
           │                    │ UpdatedAt            │
           │                    └────┬──────┬──────┬───┘
           │                         │      │      │
           │              ┌──────────┘      │      └──────────────┐
           │              │                 │                     │
           ▼              ▼                 ▼                     ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────┐
│     Prices       │ │  BomStructures   │ │   Inventories    │ │    Orders    │
│──────────────────│ │──────────────────│ │──────────────────│ │──────────────│
│ Id         PK    │ │ Id         PK    │ │ Id         PK    │ │ Id     PK    │
│ OrgId            │ │ OrgId            │ │ OrgId            │ │ OrgId        │
│ MaterialId FK ───┘ │ ParentMaterialId │ │ MaterialId FK ───┘ │ MaterialId   │
│ SupplierId FK ────→│   FK → Materials │ │ WarehouseId      │ │ OrderQty     │
│ UnitPrice         │ │ ChildMaterialId  │ │ Quantity         │ │ DueDate      │
│ Currency          │ │   FK → Materials │ │ DataVersion      │ │ DataVersion  │
│ DataVersion       │ │ Quantity         │ │ SnapshotDate     │ │ CreatedAt    │
│ EffectiveDate     │ │ Position         │ │ CreatedAt        │ └──────────────┘
│ CreatedAt         │ │ ScrapRate        │ └──────────────────┘
└──────────────────┘ │ BomViewType      │
                     │ Level            │        ┌──────────────────────┐
                     │ ValidFrom        │        │     BomVersions      │
                     │ ValidTo          │        │──────────────────────│
                     │ VersionState     │        │ Id            PK     │
                     │ CreatedAt        │───────→│ BomId         FK ───┘
                     │ UpdatedAt        │        │ VersionNumber        │
                     └──────────────────┘        │ State                │
                                                 │ ApprovedBy    FK ──┐
┌──────────────────┐                             │ ApprovedAt          │
│    Capacities    │                             │ CreatedAt           │
│──────────────────│                             └──────────┬──────────┘
│ Id         PK    │                                        │
│ OrgId            │                             ┌──────────▼──────────┐
│ WorkCenterId     │                             │     Estimates       │
│ CapacityHours    │                             │─────────────────────│
│ DataVersion      │                             │ Id           PK     │
│ CreatedAt        │                             │ OrgId               │
└──────────────────┘                             │ BomVersionId FK ───┘
                                                 │ TotalCost           │
                                                 │ LaborHours          │
                                                 │ Notes               │
                                                 │ CreatedAt           │
                                                 │ UpdatedAt           │
                                                 └─────────────────────┘
```

#### 4.4.2 用户与审计表

```text
┌──────────────────────┐
│       Users          │
│──────────────────────│
│ Id            PK     │
│ Username      UQ     │
│ PasswordHash         │
│ Role                 │
│ OrgId                │
│ IsActive             │
│ FailedLoginAttempts  │
│ LockoutUntil         │
│ CreatedAt            │
│ LastLoginAt          │
└────┬──────────┬──────┘
     │          │
     │          └──────────────────────┐
     ▼                                 ▼
┌──────────────────┐          ┌──────────────────┐
│   UserTokens     │          │    AuditLogs     │
│──────────────────│          │──────────────────│
│ Id         PK    │          │ Id         PK    │
│ UserId     FK ───┘          │ UserId     FK ───┘
│ TokenHash        │          │ Action            │
│ ExpiresAt        │          │ TableName         │
│ IsRevoked        │          │ RecordId          │
│ CreatedAt        │          │ OldValues (JSON)  │
└──────────────────┘          │ NewValues (JSON)  │
                              │ Timestamp         │
                              └───────────────────┘

┌──────────────────┐          ┌──────────────────────┐
│    SyncLogs      │          │     AppConfig        │
│──────────────────│          │──────────────────────│
│ Id         PK    │          │ Id            PK     │
│ SyncType         │          │ Key            UQ    │
│ StartedAt        │          │ Value                │
│ CompletedAt      │          │ Description          │
│ RecordsProcessed │          │ UpdatedAt            │
│ Status           │          └──────────────────────┘
│ ErrorMessage     │
└──────────────────┘          ┌──────────────────────┐
                              │   DataSnapshots      │
┌──────────────────────┐      │──────────────────────│
│   SchemaVersions     │      │ Id            PK     │
│──────────────────────│      │ SnapshotType         │
│ SchemaVersionID PK   │      │ SnapshotData (JSON)  │
│ ScriptName           │      │ CreatedAt            │
│ Applied              │      │ Description          │
└──────────────────────┘      └──────────────────────┘
```

#### 4.4.3 外键关系摘要

| 子表 | 外键列 | 引用父表 | 基数 | 说明 |
|------|--------|---------|------|------|
| BomStructures | ParentMaterialId | Materials(Id) | M:1 | BOM 父物料 |
| BomStructures | ChildMaterialId | Materials(Id) | M:1 | BOM 子物料 |
| BomVersions | BomId | BomStructures(Id) | M:1 | BOM 版本归属 |
| BomVersions | ApprovedBy | Users(Id) | M:1 | 审批人 (可空) |
| Estimates | BomVersionId | BomVersions(Id) | M:1 | 成本估算版本 (可空) |
| Prices | MaterialId | Materials(Id) | M:1 | 价格归属物料 |
| Prices | SupplierId | Suppliers(Id) | M:1 | 报价供应商 |
| Inventories | MaterialId | Materials(Id) | M:1 | 库存归属物料 |
| Orders | MaterialId | Materials(Id) | M:1 | 订单归属物料 |
| UserTokens | UserId | Users(Id) | M:1 | 令牌归属用户 |
| AuditLogs | UserId | Users(Id) | M:1 | 操作者 (可空) |

### 4.5 种子数据规格

Sprint 1 结束前必须生成：

| 数据 | 数量 | 用途 |
|------|------|------|
| 物料主数据 | 10 万条 | 性能测试基线 |
| BOM 结构节点 | 50 万条（覆盖 5 层级） | BOM 展开性能测试 |
| 历史价格快照 | 12 个月 × 核心物料 | 价格趋势分析测试 |
| 历史库存快照 | 12 个月 × 核心物料 | 库存差异分析测试 |

---

## 5. BOM 展开引擎

### 5.1 存储模型

SQLite 中采用**邻接表**（`ParentMaterialId` → `ChildMaterialId`），原因：
- 与 BomStructures 表结构自然匹配
- 插入/移动节点 O(1)
- SQLite 负责持久化，DuckDB 负责计算

### 5.2 DuckDB 集成策略

DuckDB 以**嵌入式内存模式**运行，与 SQLite 共存于同一进程：

```
SQLite (持久化 CRUD)           DuckDB (内存分析)
  BomStructures ──load──►     内存表 BomNodes
  Materials ──load──►         内存表 Materials
  Prices ──load──►            内存表 Prices
  写入 ←── 结果回写 ──        计算结果
```

```csharp
// IBomAnalysisProvider — DuckDB 分析接口
public interface IBomAnalysisProvider
{
    DataTable ExpandBom(string itemCode, DateTime asOfDate);
    DataTable CompareVersions(string bomIdA, string bomIdB);
    DataTable AggregatePrices(DateTime from, DateTime to);
}
```

```sql
-- DuckDB: BOM 递归展开（SQLite 不支持的 CTE 递归语法）
WITH RECURSIVE BomTree AS (
    SELECT ChildMaterialId, Quantity, 0 AS Level
    FROM BomNodes
    WHERE ParentMaterialId = (SELECT Id FROM Materials WHERE Code = ?)
      AND ValidFrom <= ? AND (ValidTo IS NULL OR ValidTo > ?)
    
    UNION ALL
    
    SELECT b.ChildMaterialId, b.Quantity * bt.Quantity, bt.Level + 1
    FROM BomNodes b
    JOIN BomTree bt ON b.ParentMaterialId = bt.ChildMaterialId
    WHERE bt.Level < 20
      AND b.ValidFrom <= ? AND (b.ValidTo IS NULL OR b.ValidTo > ?)
)
SELECT m.Code, m.Name, bt.Level, bt.Quantity, m.Unit
FROM BomTree bt
JOIN Materials m ON bt.ChildMaterialId = m.Id
ORDER BY bt.Level;
```

**数据加载策略**:
- DuckDB 启动时从 SQLite 加载全量数据到内存（10 万物料 + 50 万节点 ≈ 100MB）
- 同步完成后刷新 DuckDB 内存表
- 离线模式下 DuckDB 使用最后一次加载的数据（仍在内存中）

### 5.3 BOM 展开算法

```
BOMEXPAND(rootMaterialCode, [asOfDate], [versionState])
  → DuckDB 执行 WITH RECURSIVE CTE 查询
  → 按 Level 排序，返回扁平化结果
```

**算法复杂度**: DuckDB 列式引擎上递归 CTE 接近 O(N)，无需应用层层序遍历

### 5.4 UDF 输出格式

| 列 | 类型 | 说明 |
|----|------|------|
| Level | int | 层级（0 = 根节点） |
| ItemCode | string | 物料编码 |
| Description | string | 物料描述 |
| Quantity | double | 用量 |
| Unit | string | 单位 |
| Source | string | Make/Buy |

---

## 6. 差异分析引擎

### 6.1 比对维度

| 维度 | 输入 | 计算方式 |
|------|------|---------|
| **版本差异** | BOM Version A vs B | 结构化 diff（新增/删除/修改/数量变化节点） |
| **时间差异** | 同一 BOM 在 T1 vs T2 时间点 | 利用 `ValidFrom`/`ValidTo` 获取两时刻快照后 diff |
| **价格差异** | 物料 × 供应商 × 时间段 | `UnitPrice_t2 - UnitPrice_t1` × 数量 |
| **库存差异** | 物料 × 仓库 × 时间段 | `Quantity_t2 - Quantity_t1` |
| **预算差异** | Estimate vs Actual | 逐行比对（物料成本 + 人工 + 费用） |
| **供应商差异** | 同一物料不同供应商 | 价格、交付时间、质量评分对比 |

### 6.2 预警规则引擎

```text
规则 Schema: { 维度, 运算符, 阈值, 严重级别 }
示例:
  - 价格波动 > 10% → Warning
  - 库存低于 安全库存 × 0.5 → Critical
  - BOM 结构变更 → Info（仅审计）
  - 预算偏差 > 15% → Error
```

### 6.3 输出格式（Excel）

- 差异单元格: 条件格式（绿色=低于阈值 / 黄色=接近阈值 / 红色=超过阈值）
- 差异摘要: 单单元格文本（Excel 2019）或结构化数组（Excel 365）

---

## 7. WPF 仪表盘规格

### 7.1 窗口生命周期

```
用户点击 Ribbon "打开仪表盘"
  → 检查是否已有仪表盘实例
    → 否: 创建新 STA 线程 → 启动 WPF Application → 加载数据 → 显示窗口
    → 是: 激活已有窗口
  → 用户关闭窗口: 隐藏（非销毁），保持数据缓存
  → Excel 关闭: 通知仪表盘线程退出 → 保存布局状态
```

### 7.2 技术选型

| 组件 | 推荐方案 | 备选 |
|------|---------|------|
| **图表** | LiveCharts2（支持 .NET Framework 4.7.2+） | OxyPlot / ScottPlot |
| **架构模式** | MVVM（CommunityToolkit.Mvvm） | Prism |
| **数据绑定** | `ObservableCollection` + 定时刷新（默认 5s）或事件驱动（从 EventBus） | — |
| **跨线程 Excel 交互** | `ExcelThreadDispatcher.RunOnExcelThreadAsync` | — |

### 7.3 看板内容

| 面板 | 内容 | 刷新策略 |
|------|------|---------|
| KPI 概览 | 物料总数、活跃 BOM 数、待审批数、预警数 | 进入时加载 + 手动刷新 |
| 差异趋势图 | 近 12 个月价格/库存差异变化 | 事件驱动 + 定时 30s |
| 预警列表 | 当前触发的预警（按严重级别排序） | 实时（事件驱动） |
| BOM 结构树 | 选中物料的完整 BOM 树 | 按需加载 |

---

## 8. UDF 函数清单

> **设计意图与分类见本节。完整 API 合约（参数约束、错误码、示例）见 [api-reference.md](./api-reference.md) —— 后者为权威参考。**

| # | 函数名 | 用途 | 分类 | 详细规格 |
|---|--------|------|------|---------|
| 1 | `BOMEXPAND` | 展开物料完整 BOM 结构 | BOM 查询 | [§3.1](./api-reference.md#31-bomexpand) |
| 2 | `BOMCOST` | 计算物料汇总成本 | BOM 查询 | [§3.2](./api-reference.md#32-bomcost) |
| 3 | `PRICELOOKUP` | 查询物料供应商价格 | 数据查询 | [§3.3](./api-reference.md#33-pricelookup) |
| 4 | `INVENTORYQTY` | 查询物料当前库存量 | 数据查询 | [§3.4](./api-reference.md#34-inventoryqty) |
| 5 | `VARIANCECHECK` | 比较两个 BOM 版本的差异 | 差异分析 | [§3.5](./api-reference.md#35-variancecheck) |
| 6 | `ALERTCHECK` | 检查物料的预警状态 | 差异分析 | [§3.6](./api-reference.md#36-alertcheck) |
| 7 | `ORDERSTATUS` | 查询物料订单状态 | 数据查询 | [§3.7](./api-reference.md#37-orderstatus) |
| 8 | `SYNCSTATUS` | 获取当前数据同步状态 | 系统状态 | [§3.8](./api-reference.md#38-syncstatus) |

### 8.1 设计约束

- **线程安全**: 纯查询函数标记 `IsThreadSafe = true`（Excel 可并行计算）；`SYNCSTATUS` 读 COM 状态除外
- **波动性**: 仅 `SYNCSTATUS` 为易变函数（`IsVolatile = true`），其余均为非易变
- **版本兼容**: 数组返回函数（`BOMEXPAND`, `VARIANCECHECK`）对 Excel 2019 使用 `Ctrl+Shift+Enter` 模式，Excel 365 使用动态溢出。详见 [§13](./specification.md#13-excel-版本兼容矩阵)

---

## 9. 同步服务规格

### 9.1 IErpAdapter 接口

```csharp
public interface IErpAdapter
{
    Task<IEnumerable<Material>> PullMaterialsAsync(DateTime? since = null);
    Task<IEnumerable<PriceRecord>> PullPricesAsync(DateTime? since = null);
    Task<IEnumerable<InventoryRecord>> PullInventoriesAsync(DateTime? since = null);
    Task<IEnumerable<OrderRecord>> PullOrdersAsync(DateTime? since = null);
    Task<IEnumerable<CapacityRecord>> PullCapacitiesAsync(DateTime? since = null);
    Task<bool> TestConnectionAsync();
}
```

### 9.2 重试策略配置

```csharp
// 简单指数退避重试（无第三方依赖）
private const int MaxRetries = 3;

private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
{
    for (int attempt = 1; ; attempt++)
    {
        try { return await action().ConfigureAwait(false); }
        catch (InvalidOperationException) { throw; } // 不重试业务错误
        catch (Exception ex) when (attempt < MaxRetries)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                      + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            AppLogger.Warn($"重试 {attempt}/{MaxRetries}: {ex.Message}");
            await Task.Delay(delay).ConfigureAwait(false);
        }
    }
}
```

### 9.3 批量同步流程

```
1. TestConnection() → 不通? → 标记离线模式 → 结束（本地 SQLite 仍可读写）
2. 读取上次同步时间戳 (SyncLogs)
3. 并行拉取: Materials + Prices + Inventories + Orders + Capacities
4. 写入本地 SQLite (事务)
5. 刷新 DuckDB 内存表（从更新后的 SQLite 加载）
6. 更新 SyncLogs
7. 刷新 MemoryCache (L1)
8. 发布 DataRefreshedEvent → Dashboard + TaskPane 更新
```

### 9.4 Excel 导入备用通道

- 支持格式: `.xlsx`, `.xls`, `.csv`
- 列映射: 通过 JSON 配置文件定义 `{ "ExcelColumn": "DBColumn" }`
- 校验: 导入前数据验证（必填字段、类型检查、外键存在性）

---

## 10. 离线模式规格

### 10.1 状态模型

离线不是"切到备用数据库"——SQLite 和 DuckDB 始终可用的本地引擎。"离线"仅意味着**无法与 ERP 同步**。

| 模式 | SQLite CRUD | DuckDB 分析 | ERP 同步 | 编辑 |
|------|:--:|:--:|:--:|:--:|
| **在线** | ✅ 本地读写 | ✅ 内存计算 | ✅ 定时/手动 | ✅ |
| **离线** | ✅ 本地读写 | ✅ 内存计算 | ❌ 暂停 | ✅ |

> **V1.0 红线修正**: 原方案"离线仅限只读"是基于服务器数据库断连的假设。采用本地 SQLite+DuckDB 后，数据库始终在线，只有 ERP 同步通道断开。编辑操作作用于本地 SQLite，网络恢复后通过同步服务上行变更。

### 10.2 状态机

```text
         启动 ──► 运行中（SQLite+DuckDB 始终可用）
                    │
                    ├─ ERP 可达 → 在线（定时同步 + 用户编辑）
                    │
                    └─ ERP 不可达 → 离线（用户编辑正常，同步暂停）
                                      │
                                      └─ 网络恢复 → 增量同步 → 刷新 DuckDB
```

### 10.3 离线行为

| 行为 | 在线（ERP 可达） | 离线（ERP 不可达） |
|------|-----------------|-------------------|
| **数据读取** | SQLite + DuckDB 本地 | 同左（无变化） |
| **数据写入** | ✅ 本地 SQLite → 下次同步上行 | ✅ 本地 SQLite → 网络恢复后上行 |
| **UDF 计算** | DuckDB 内存计算 | 同左（无变化） |
| **Dashboard** | 实时 | 同左（显示"离线数据 (更新于: yyyy-MM-dd HH:mm)"） |
| **同步** | 定时 + 手动触发 | 暂停，网络恢复后自动执行 |

### 10.3 网络检测

```csharp
// 使用 Windows Network List Manager API 或周期性 ping
public class NetworkMonitor
{
    // 方式 1: System.Net.NetworkInformation
    public static bool IsNetworkAvailable() =>
        NetworkInterface.GetIsNetworkAvailable();

    // 方式 2: 主动探测（更可靠）
    public async Task<bool> ProbeConnectionAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(_healthCheckUrl);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
```

### 10.4 SQLite 与 DuckDB 协作

```sql
-- 缓存表结构与业务表一致，增加同步元数据
CREATE TABLE Cache_Materials (
    -- 业务字段省略（与 Materials 表一致）
    LastSyncedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 缓存元数据
CREATE TABLE CacheMetadata (
    TableName TEXT PRIMARY KEY,
    LastSyncTimestamp DATETIME,
    RecordCount INT,
    SyncStatus TEXT  -- 'Complete', 'Partial', 'Error'
);
```

### 10.5 离线编辑（V1.0 更新）

> **v1.1 变更**: SQLite 本地主库使离线编辑成为 V1.0 范围——用户始终操作本地 SQLite，同步服务负责上行变更。无需 V2.0 额外开发。

---

## 11. 安全规格

### 11.1 认证流程

```
用户输入 用户名 + 密码
  → BLL.AuthService.Authenticate(username, password)
    → DAL 查询 Users 表
    → BCrypt.Verify(password, storedHash)
      → 失败: 记录尝试次数，超过阈值锁账户
      → 成功: 生成 JWT Token
        → 写入 UserTokens（含过期时间）
        → 返回 Token 给客户端
  → 后续请求: HTTP Header "Authorization: Bearer {token}"
    → 验证签名 + 过期时间 + 吊销状态
```

### 11.2 授权矩阵

| 角色 | 查看 BOM | 编辑 BOM | 审批 BOM | 价格查看 | 系统配置 | 审计查看 |
|------|---------|---------|---------|---------|---------|---------|
| **Admin** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Analyst** | ✅ | ✅ | ❌ | ✅ | ❌ | 仅自己 |
| **Viewer** | ✅ | ❌ | ❌ | 脱敏 | ❌ | ❌ |

### 11.3 加密策略

| 场景 | 算法 | 密钥管理 | 状态 |
|------|------|---------|------|
| **传输** | TLS 1.2+ / HTTPS | 标准证书链 | 远期（当前为本地模拟） |
| **价格数据（静态）** | AES-256-CBC | DPAPI 保护 DEK | 远期（V2.0，代码已就绪但未启用） |
| **密码** | BCrypt + Salt（work factor ≥ 12） | 无（单向哈希） | **已实现** |
| **SQLite 离线缓存** | 磁盘级 BitLocker | 系统级 | 环境级 |

### 11.4 审计日志

- **覆盖范围**: 所有增删改操作（SELECT 不记录）
- **记录内容**: `{ UserId, Action(CREATE/UPDATE/DELETE), TableName, RecordId, OldValues(JSON), NewValues(JSON), Timestamp}`
- **实现方式**: AOP 拦截（ActionFilter 或 Castle DynamicProxy 或手动包装）
- **保留期限**: ≥ 1 年
- **显示脱敏**: 非授权用户看到价格字段时显示 `****` 或 `⚠️ 无权查看`

### 11.5 合规

- **GDPR**: 支持"被遗忘权"——用户请求后 30 天内删除所有关联数据
- **ISO 27001**: 访问控制（RBAC）、审计日志、加密传输/存储、定期安全扫描

---

## 12. 性能与可扩展性

### 12.1 性能 KPI

| 指标 | 目标值 | 测量方法 |
|------|--------|---------|
| 插件加载（首次） | <3s | AutoOpen 到 Ribbon 显示时间 |
| 插件加载（后续） | <1s | 热启动 |
| UDF 单次响应 | <100ms | 代表性 UDF 99 分位 |
| BOM 展开（1000 节点） | <500ms | BenchmarkDotNet |
| 差异看板首次加载 | <3s | WPF Performance Toolkit |
| 数据同步（10 万条） | <5min | 端到端计时 |
| 审计日志写入延迟 | <50ms | AOP 拦截前后时间差 |
| 内存占用（正常） | <200MB | dotMemory / Process Explorer |
| 内存占用（大数据集） | <500MB | dotMemory / Process Explorer |
| SLA | 99.5%（月度） | 生产监控 |

### 12.2 Cache 分层策略

| 层级 | 存储 | TTL | 用途 |
|------|------|-----|------|
| **L0** | UDF 计算链内局部变量 | 单次计算生命期 | 同工作表中同一 UDF 重复调用去重 |
| **L1** | `MemoryCache`（进程内） | 30s ~ 5min（按数据类型） | 频繁访问的物料、价格、库存数据 |
| **L2** | SQLite 磁盘缓存 | 最近一次成功同步 | 离线模式数据源 |

### 12.3 性能测试方法

- **工具**: BenchmarkDotNet（微基准）、dotMemory（内存分析）、PerfView（ETW 跟踪）
- **数据规模**: 10 万物料 + 50 万 BOM 节点（Sprint 1 起可用）
- **CI 门禁**: 性能回归——BOM 展开时间增量 > 10% 则 CI 告警

---

## 13. Excel 版本兼容矩阵

### 13.1 功能 × 版本

| 功能 | Excel 2019 | Excel 365 | 适配策略 |
|------|-----------|-----------|--------|
| **BOM 展开** | `Ctrl+Shift+Enter` 数组公式 | 动态数组自动溢出 | 检测版本，2019 返回固定区域 + 提示 |
| **差异分析** | 单单元格返回文本摘要 | 返回结构化数组 | UDF 内部判断 `Application.Version` |
| **仪表盘** | 独立 WPF 窗口 | 同 2019 | 无差异 |
| **任务窗格** | 支持 | 支持 | 无差异 |
| **Ribbon** | 支持 | 支持 | 无差异 |

### 13.2 32/64 位差异

| 差异 | 32 位 | 64 位 | 应对 |
|------|-------|-------|------|
| 内存上限 | 2GB（进程） | 8TB（理论） | 大数据集场景推荐 64 位 |
| 数组公式行数 | 受内存限制（每列约 100 万行） | 同左 | 超大数据集提示分页 |

### 13.3 版本检测代码

```csharp
public class VersionAdapter : IVersionAdapter
{
    public bool IsDynamicArraySupported
    {
        get
        {
            var app = (Application)ExcelDnaUtil.Application;
            // Excel 365 版本号 ≥ 16.0.12026
            var version = Version.Parse(app.Version);
            return version >= new Version(16, 0, 12026);
        }
    }
}
```

---

## 14. 错误处理与诊断

### 14.1 错误分类

| 类别 | 呈现方式 | 示例 |
|------|---------|------|
| **用户可见错误** | Excel 单元格 `#VALUE!` / `#N/A` + 任务窗格消息 | "物料编码 123456 不存在" |
| **开发者可见错误** | AppLogger 文件日志 + CorrelationId 关联 | "同步超时: ERP 端点无响应 (CorrelationId: abc-123)" |
| **静默错误** | AppLogger 文件日志，不打扰用户 | "后台重试已耗尽 (3/3)，将在 5 分钟后恢复" |

### 14.2 日志配置

```text
日志策略（AppLogger + FileLogSink）：
- 输出通道: Debug.WriteLine（全量）+ 文件（Warn+ 全量，Info 1% 采样）
- 文件位置: %LocalAppData%/BomAddIn/Logs/{yyyy-MM-dd}.log
- 格式: {timestamp} | {LEVEL} | {logger} | {message}
- 敏感字段: 价格数据不写入日志，密码字段始终脱敏
```

### 14.3 诊断工具检查清单

`BomAddIn.Diagnostic.exe` 检查项：

1. .NET Framework / .NET 版本
2. Excel 安装版本与位数（32/64 bit）
3. 数据库连接（本地 SQLite）
4. SQLite 缓存文件读写权限
5. 网络连通性（ERP API 端点）
6. 用户配置文件完整性
7. 插件注册状态
8. 最近 5 条错误日志

---

## 附录 A: 参考来源与改进说明

本规格基于原始 `Plan and Specification.txt v4.0` 整合并增强：

- **DI 设计**: 原文档缺失，参考 [Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)
- **UDF 线程策略**: 参考 [Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs) 和 Excel-DNA 社区最佳实践
- **BOM 架构范式**: 参考 [OpenBOM](https://www.openbom.com) 的 xBOM 多视图和 Reference-Instance 分离模型
- **Excel 线程安全**: 参考 [DotNetRefEdit](https://github.com/Ron-Ldn/DotNetRefEdit) 和 [Excel-DNA DeepWiki](https://deepwiki.com/Excel-DNA/ExcelDna/)
- **`ExcelThreadDispatcher` 死锁修复**: 原文档同步版本存在死锁风险，本文档修复了调用线程检测逻辑

> 📋 **配对文档**: 重构路线图、阶段计划、风险管理请参见 [refactoring-plan.md](./refactoring-plan.md)
