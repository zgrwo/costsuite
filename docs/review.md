# Review 意见：Plan and Specification.txt v4.0 技术评审

> **文档类型**: 独立 Review 意见（历史评审快照）  
> **日期**: 2026-07-12  
> **受众**: 架构师、技术负责人  
> **状态**: 📸 只读快照。文档中建议的改进已整合至 [specification.md](./specification.md) 和 [plan.md](./plan.md)。保留本文档用于追溯"为什么做了这些改进"。  
> **配套文档**: [plan.md](./plan.md) / [specification.md](./specification.md)

---

## 一、总体评价

该方案是一份**工程纪律非常出色**的企业级项目文档，在以下方面明显优于 GitHub 上可对比的开源项目：

- ✅ **技术探针制度化**（Sprint 0 门禁）—— 所有可对比开源项目均无此实践
- ✅ **线程安全作为基础设施**而非注意事项 —— 提供了 `ExcelThreadDispatcher` 代码范式 + 2h 压力测试
- ✅ **离线降级策略** —— Excel 插件中极为少见的离线只读设计
- ✅ **种子数据前置到 Sprint 1** —— 避免"开发环境快、生产慢"
- ✅ **5 类测试全覆盖** —— 线程安全和兼容性测试在开源项目中极为罕见

但在**技术规格的精确性**方面存在显著短板 —— BOM 算法、数据模型细节、API 契约、UDF 签名等核心规格停留在概念层面，这是许多开源项目至少用代码表达的部分。

---

## 二、GitHub 可对比项目分析

### 2.1 项目清单

| 项目 | Stars | 类型 | 关联度 | 值得借鉴之处 |
|------|-------|------|--------|-------------|
| **[Excel-DNA/ExcelDna](https://github.com/Excel-DNA/ExcelDna)** | 1,483 | 框架 | ⭐⭐⭐ | 底层框架，本项目的技术基石 |
| **[FinAnSu](https://github.com/brymck/finansu)** | — | Excel-DNA 金融 | ⭐⭐⭐ | Ribbon UI、RTD 实时数据、WPF 集成的最佳实践集合 |
| **[Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs)** | — | Excel-DNA UDF | ⭐⭐⭐ | `IsThreadSafe` + `ExcelReference` 的高性能 UDF 范式 |
| **[Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)** | — | Excel-DNA 示例 | ⭐⭐⭐ | **DI 集成**、Ribbon 控制、安装程序模板 |
| **[DotNetRefEdit](https://github.com/Ron-Ldn/DotNetRefEdit)** | — | Excel-DNA WPF | ⭐⭐⭐ | WPF 独立线程 + 自定义消息泵 + click-twice 修复 |
| **[Inventory_v01](https://github.com/petersonmatiss/Inventory_v01)** | — | 库存+BOM | ⭐⭐⭐ | BOM Excel 导入（智能列检测）、.NET 9 + EF Core 技术栈 |
| **[xlDuckDb](https://github.com/RusselWebber/xlDuckDb)** | 88 | Excel-DNA 分析 | ⭐⭐ | DuckDB 嵌入式分析引擎集成，OLAP 场景参考 |
| **[DBAddin](https://github.com/rkapl123/DBAddin)** | 22 | Excel-DNA 数据库 | ⭐⭐ | 数据库 UDF 封装模式 |
| **[Open Industry Project](https://github.com/Open-Industry-Project/Open-Industry-Project)** | 364 | 制造仿真 | ⭐⭐ | 制造领域 C# 架构参考 |
| **OpenBOM** | 商业 | SaaS BOM | ⭐⭐⭐ | **图数据库 + xBOM 多视图 + Reference-Instance** — 架构范式级参考 |
| **[Excel-DNA/IntelliSense](https://github.com/Excel-DNA/IntelliSense)** | 179 | Excel-DNA 增强 | ⭐⭐ | UDF IntelliSense 提升用户体验 |
| **[PLMore](https://github.com/PLMore)** | — | PLM | ⭐⭐ | EBOM/MBOM/SBOM 多视图模型 |

### 2.2 关键对比发现

#### 🟢 本方案的优势

| 维度 | 本方案 | 开源项目现状 |
|------|--------|-------------|
| 技术探针制度 | Sprint 0 强制门禁，不通过则项目暂停 | 无项目有此实践 |
| 线程安全制度化 | `ExcelThreadDispatcher` 代码范式 + 2h 压力测试 | FinAnSu/DotNetRefEdit 有最佳实践但未制度化 |
| 离线降级策略 | 明确状态机 + 只读边界 | 社区无成熟 SQLite 离线同步方案 |
| 种子数据前置 | Sprint 1 即 10 万+50 万数据 | 所有项目均无此实践 |
| 测试覆盖维度 | 5 类（单元+集成+线程+兼容+性能）| 多数仅基本单元测试 |

#### 🟡 可借鉴的外部实践

| 来源 | 实践 | 本方案现状 | 建议 |
|------|------|-----------|------|
| **OpenBOM** | 图数据库 (Neo4j) 存储 BOM 结构 | 关系型（未指定引擎） | 百万级节点下递归 CTE 性能远不如图遍历；建议 spec 中评估图 DB 作为 V2.0 升级路径 |
| **OpenBOM** | xBOM 多视图（EBOM/MBOM/SBOM）| 仅"BOM 版本化" | 建议明确定义 BOM 视图类型及转换规则 |
| **OpenBOM** | Reference-Instance 分离 | Materials + BomStructures 两张表 | 同一物料在多 BOM 中出现时数据冗余；建议分离 |
| **Extensibility.ExcelDNA.Sample** | `Microsoft.Extensions.DependencyInjection` | 未提及 DI | **关键缺失**：六层架构无 DI = 高耦合、难测试 |
| **FinAnSu** | RTD Server 实时数据推送 | 无实时数据能力 | 价格/库存变动可考虑 RTD 推送 |
| **Collection-of-UDFs** | `IsThreadSafe` + `ExcelReference` | 统一 QueueAsMacro 封送 | 纯计算 UDF 走 QueueAsMacro 开销大，应区分策略 |
| **DotNetRefEdit** | 独立线程 WPF + 自定义消息泵 | 提到"独立 STA 线程"但无细节 | 应明确消息泵实现，避免 click-twice 问题 |
| **Excel-DNA 社区** | `SynchronizationContext.Post` 异步 UDF 错误处理 | `RunOnExcelThreadAsync` 仅 QueueAsMacro | 异步 UDF 异常处理应捕获 UI Context |
| **Inventory_v01** | 智能 Excel 列检测导入 | "Excel 导入备用通道" | 可借鉴自动列映射算法 |

---

## 三、改进建议（按严重度分级）

### 3.1 🔴 关键问题（必须处理）

#### I-1: `RunOnExcelThread` 死锁风险

**位置**: 原文档 2.2 节代码示例  
**问题**: `RunOnExcelThread<T>` 同步版本无条件调用 `ExcelAsyncUtil.QueueAsMacro`。如果调用方本身就在 Excel 主线程上，`QueueAsMacro` 会将任务放入消息队列等待当前执行完成，而当前执行又在等待返回值 — **死锁**。

**修复**: 已在 [specification.md §3.1](./specification.md#31-iexcelthreaddispatcher) 中修复，增加 `IsExcelMainThread` 检测。

```csharp
public T RunOnExcelThread<T>(Func<T> action)
{
    if (IsExcelMainThread) return action(); // 已在主线程，直接执行
    return ExcelAsyncUtil.QueueAsMacro(() => action());
}
```

---

#### I-2: 缺少依赖注入设计

**问题**: 六层架构无 DI 容器会致硬编码依赖，严重削弱可测试性和可维护性。  
**参考**: [Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample) 已完整演示 DI 集成模式。  
**修复**: 已在 specification.md §2.3 中补充。

---

#### I-3: 数据库引擎未指定

**问题**: DbUp、Dapper、读写分离、TestContainers、SQLite 缓存的选型都依赖数据库引擎决策。Document 暗示 SQL Server（DbUp 典型搭配），但未确认。  
**建议**: 优先确认引擎。若为 SQL Server，需明确版本（2019+）；若为 PostgreSQL，需改用 Npgsql。此决策应在 Sprint 0 完成。

---

#### I-4: BOM 展开算法未定义

**问题**: "多维差异分析"概念模糊，"BOM 展开"未指定存储模型和遍历算法。  
**影响**: 若选错算法，10 万节点下可能从 <500ms 退化到 30s+。  
**建议**: 已在 specification.md §5 中选定邻接表 + 层序遍历方案。百万级节点时评估嵌套集或图数据库（Neo4j）升级。

---

#### I-5: ERP 接口协议空白

**问题**: 适配器模式仅是占位符，无 REST/gRPC/ODBC 决策、无 payload 格式、无认证方式。  
**建议**: Sprint 2 结束前与客户 IT 确定 ERP API 规格；Sprint 3 前完成 `IErpAdapter` 第一个具体实现。

---

### 3.2 🟡 重要问题（强烈建议）

#### I-6: 缺少事件总线

**问题**: WPF Dashboard、UDF、Ribbon、Sync 之间的数据变更通知需要 pub-sub 解耦。  
**建议**: 已在 specification.md §2.4 中建议使用 `System.Reactive` Subject 或轻量 Mediator。

---

#### I-7: Cache 分层未设计

**问题**: "MemoryCache"一笔带过，无 L0/L1/L2 分层、TTL、失效策略。  
**建议**: 已在 specification.md §12.2 中设计三层缓存架构。

---

#### I-8: Polly 参数未定义

**问题**: "Polly 重试"未指定重试次数、退避策略、超时、熔断参数。  
**建议**: 已在 specification.md §9.2 中补全（3 次重试、指数退避 1s/2s/4s、熔断 5 次/30s）。

---

#### I-9: UDF 函数清单缺失

**问题**: "至少 8 个 UDF"但 0 个定义签名。  
**建议**: 已在 [specification.md §8](./specification.md#8-udf-api-reference) 中补全 8 个函数的完整 API Reference。

---

#### I-10: 快照 Schema 未定义

**问题**: DataSnapshots 是离线模式+差异回溯的骨干，但未说明内容（全量 BOM 树导出？增量变更？元数据指针？）。  
**建议**: 已在 specification.md §10.4 中补充 SQLite 缓存 Schema 和元数据表设计。

---

#### I-11: UDF 线程策略一刀切

**问题**: `ExcelThreadDispatcher` 统一封送所有 UDF。纯计算的 UDF（如价格查询、库存查询）可以直接标记 `IsThreadSafe = true` 让 Excel 并行计算，不必走 QueueAsMacro 的额外开销。  
**参考**: [Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs) 区分了计算密集型 vs. UI 修改型 UDF。  
**建议**: 已在 [specification.md §8](./specification.md#8-udf-api-reference) 中按函数标注了线程安全策略。

---

### 3.3 🟠 增强建议

#### I-12: xBOM 多视图

借鉴 OpenBOM，定义 EBOM（设计 BOM）/ MBOM（制造 BOM）/ CBOM（成本 BOM）视图。当前仅"BOM 版本化"不够。这在多部门协作场景中极为关键。

#### I-13: Reference-Instance 分离

将 Material 定义（不变属性：编码、名称、规格）与 BOM 实例（可变属性：用量、位置）分离。当前模型会在同一物料被多 BOM 引用时产生冗余。

#### I-14: RTD Server 实时推送

借鉴 FinAnSu 的 RTD 模式，对价格/库存变动提供实时推送选项。当前无实时数据能力，用户需手动刷新。

#### I-15: 多工作簿并发模型

Excel 可同时打开多个工作簿，各自可能触发 UDF 重算和同步操作。当前未定义竞态处理策略。

#### I-16: 安全密钥管理

AES-256 密钥存储方案未指定。建议使用 Windows DPAPI (`ProtectedData.Protect`) 保护数据加密密钥（DEK）。

#### I-17: Sprint 调整

| 问题 | 建议 |
|------|------|
| Sprint 0 1 周过载（CI + 3 探针） | 扩展至 2 周，或缩窄至仅线程探针 |
| Sprint 5 4 功能挤 2 周 | 审批工作流移至 Sprint 3（与 BOM 版本化配对），快照移至 Sprint 4（与 Dashboard 历史视图配对），Sprint 5 专注审计日志+性能优化 |

---

## 四、缺失项清单（Gap Inventory）

| # | 缺失项 | 严重度 | 在 spec 中补充? |
|---|--------|--------|----------------|
| 1 | ERP API 契约（协议、格式、认证） | 🔴 关键 | ⬜ 需客户确认 |
| 2 | 依赖注入设计 | 🔴 关键 | ✅ 已补充 (§2.3) |
| 3 | 数据库引擎确认 | 🔴 关键 | ⬜ 需决策 |
| 4 | `ExcelThreadDispatcher` 死锁修复 | 🔴 关键 | ✅ 已补充 (§3.1) |
| 5 | BOM 展开算法定义 | 🔴 关键 | ✅ 已补充 (§5) |
| 6 | UDF 函数签名清单 | 🟡 重要 | ✅ 已补充 (§8) |
| 7 | 事件总线/消息机制 | 🟡 重要 | ✅ 已补充 (§2.4) |
| 8 | Cache 分层策略 | 🟡 重要 | ✅ 已补充 (§12.2) |
| 9 | Polly 策略参数 | 🟡 重要 | ✅ 已补充 (§9.2) |
| 10 | 快照 Schema | 🟡 重要 | ✅ 已补充 (§10.4) |
| 11 | UDF 线程策略区分 | 🟡 重要 | ✅ 已补充 (§8) |
| 12 | 多工作簿并发模型 | 🟠 中等 | ⬜ 待补充 |
| 13 | 安全密钥管理方案 | 🟠 中等 | ✅ 已补充 (§11.3) |
| 14 | 本地化/i18n 方案 | 🟠 中等 | ⬜ 待补充 |
| 15 | WPF 消息泵实现细节 | 🟠 中等 | ⬜ 待补充 |
| 16 | 错误处理分类体系 | 🟠 中等 | ✅ 已补充 (§14) |
| 17 | ER 图 | 🟠 中等 | ⬜ 待补充（需工具绘制） |
| 18 | 数据库引擎确认后的具体 DDL | 🟠 中等 | ⬜ 引擎确认后可补充 |
| 19 | COM add-in 共存风险 | 🟠 中等 | ✅ 已补充（plan.md 风险 R8） |
| 20 | 数据主权/驻留风险 | 🟠 中等 | ✅ 已补充（plan.md 风险 R9） |
| 21 | AV 白名单风险管理 | 🟠 中等 | ✅ 已补充（plan.md 风险 R6） |
| 22 | UAT 计划 | 🟠 中等 | ✅ 已补充（plan.md §7.2） |

---

## 五、文档拆分说明

原始 `Plan and Specification.txt v4.0` 已拆分为：

| 文件 | 内容 | 原始来源 |
|------|------|----------|
| **[plan.md](./plan.md)** | 实施计划：Sprint 路线图、风险登记册、CI/CD、部署、测试策略、UAT | §1, §4, §5, §6, §8 |
| **[specification.md](./specification.md)** | 技术规格：架构、数据模型、线程安全、离线模式、UDF API、安全、性能 | §2, §3, §7, §9 |
| **[review.md](./review.md)**（本文档） | 独立 Review：GitHub 对比分析、改进建议、缺失项清单 | —（新增） |

### 拆分原则

- **plan.md** 不含技术实现细节（代码示例、接口定义、算法描述）
- **specification.md** 不含时间线和资源规划（Sprint 周期、角色人数、里程碑日期）
- **review.md** 不含规格内容，仅含评审意见和改进建议
- 代码示例统一保存在 **specification.md** 中
- 补充的内容已直接整合进 spec/plan，而非保留为 TODO 注释
- 两份文档通过 Sprint 编号交叉引用

---

## 六、后续行动建议

### 立即行动（Sprint 0 前）
1. ⬜ **确认数据库引擎**（SQL Server vs PostgreSQL）— 所有 DDL/TestContainers 选型依赖此决策
2. ⬜ **与客户 IT 沟通 AV 白名单** — 避免上线最后一周被拦截
3. ⬜ **明确 ERP API 协议** — `IErpAdapter` 的接口设计依赖此信息

### Sprint 0 交付
4. ✅ DI 容器集成（参考 spec §2.3）
5. ✅ `ExcelThreadDispatcher` 含主线程检测（参考 spec §3.1）
6. ✅ Excel 版本探针验证（参考 spec §13.3）

### Sprint 1-2 补充
7. ⬜ 绘制 ER 图（基于 spec §4 的表定义）
8. ⬜ 生成具体 DDL 脚本（确认引擎后）
9. ⬜ 确定 WPF 图表库（LiveCharts2 / OxyPlot / ScottPlot）

### Sprint 3-4 补充
10. ⬜ 多工作簿并发控制方案
11. ⬜ WPF 消息泵实现（参考 DotNetRefEdit）
12. ⬜ 本地化框架选型

---

> 📋 **配套文档**: [plan.md](./plan.md)（实施计划） / [specification.md](./specification.md)（技术规格）  
> 📅 **下次评审**: Sprint 0 结束，重点验证关键技术探针结果
