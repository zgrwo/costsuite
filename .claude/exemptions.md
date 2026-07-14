# Exemptions

> **范围**: 仅登记已知但经评审决定**不修复**的问题。  
> **不在此处**: 待修复问题 → Sprint Backlog / Issue Tracker。  
> **为什么需要这份文件**: 每一个豁免决定都是一次风险接受——记录"谁、为什么、什么条件下重开"，防止将来遗忘。

---

## 豁免登记表

| ID | 模块 | 文件 | 函数 | 行号 | 问题摘要 | 发现日 | 来源 | 豁免理由 | 已接受风险 | 过期条件 | 审批人 | 审批日 |
|----|------|------|------|:---:|----------|--------|:---:|----------|-----------|----------|--------|--------|
| — | — | — | — | — | 暂无 | — | — | — | — | — | — | — |
| EX-001 | Infrastructure | src/BomAddIn.Infrastructure/Models/ | 全部 | — | Domain Models 从 Core 移至 Infrastructure 以解决依赖链问题 | 2026-07-12 | Sprint 1 | 依赖链 BomAddIn→Core→Data→Infra 中，Data 层 Repository 需访问 Model 类型但无法反向引用 Core。将纯 POCO Model 放在链底部 Infra 中使全链可访问。POCO 无业务逻辑，不实质违反"Infra 不知业务"原则。 | 将来如需分离 Domain 程序集，需重构。V2.0 可创建 BomAddIn.Domain。 | V2.0 架构升级时 | — | 2026-07-12 |
| EX-002 | Core/Infra | src/BomAddIn.Infrastructure/Models/BomExpandedNode.cs | — | — | BomExpandedNode（DuckDB CTE 查询结果 DTO）置于 Infrastructure/Models，与 BomNode 同目录。该类型是扁平 DTO 而非业务实体，放在 Infra 中以供 Data 层 DuckDB 查询结果映射。 | DTO 类型与 Domain Model 混放，未来如果区分 DTO/Entity 需迁移。 | V2.0 架构升级时 | — | 2026-07-12 |
| EX-003 | Data | src/BomAddIn.Data/Sync/IErpAdapter.cs | — | — | IErpAdapter 接口定义在 Data 层而非 Core 层。原因是它仅被 SyncService（Core）引用，且其返回类型是 Infrastructure 的实体模型。放在 Data 层避免了 Core→Data 的反向依赖。 | V1.0 中 IErpAdapter 是同步适配器的抽象，如果未来加入真正的 REST/GraphQL 客户端，接口需提升到 Core 层。 | V2.0 ERP 集成重构时 | — | 2026-07-12 |
| EX-004 | BomAddIn | src/BomAddIn/BomAddIn-AddIn64.dna | — | — | Excel-DNA ExternalLibrary 在 net472 下存在数量上限（~22 个）。超量导致 Excel 静默崩溃，无日志。与具体库无关，是 Excel-DNA 1.8.0 内部限制。规避方案：重量级依赖（DuckDB, Polly, ExcelDataReader）用 Reference 延迟加载。 | 超过 22 个 ExternalLibrary 时 Excel 无法启动。在 Excel-DNA 修复前需严格控制 ExternalLibrary 数量。 | Excel-DNA 版本升级到 1.9+ 或发现根本原因有修复时 | — | 2026-07-15 |
| EX-005 | BomAddIn | src/BomAddIn/Bootstrap/AutoOpen.cs | RegisterTaskPane() | — | CustomTaskPaneFactory.CreateCustomTaskPane + WPF UserControl 在 AutoOpen 中直接调用时，WPF 资源未完全就绪导致 Excel 崩溃。当前禁用此功能。 | WPF TaskPane 不可用，用户无法通过右侧面板查看同步状态。 | WPF 初始化在 Excel-DNA 加载链中的时序确认安全后 | — | 2026-07-15 |

> 登记时复制下行：
> `| EX-### | 程序集 | src/.../File.cs | Method() | L# | 一句话 | YYYY-MM-DD | CR | 理由 | 接受的风险 | 重开条件 | 审批人 | YYYY-MM-DD |`

---

## 豁免详情

> 当表格一行不足以说明决策背景时，在此展开。`EX-###` 与上表 ID 对应。

<!--
### EX-001: 标题

| 字段 | 内容 |
|------|------|
| **问题描述** | |
| **复现条件** | |
| **实际行为** | |
| **预期行为** | |
| **为何不修** | {技术代价过大？V1.0 范围外？上游依赖未就绪？用户影响可控？} |
| **已接受风险** | {不修会导致什么后果，影响哪些用户场景} |
| **替代方案** | {有没有 workaround？文档告知用户？配置项关闭？} |
| **过期条件** | {什么情况触发重新评估} |
| **决策人** | |
| **决策日期** | |

---

> **评审记录**:
> - YYYY-MM-DD: {Sprint # 评审结论 — 维持豁免 / 重新打开 / 已过期自动重开}
-->

---

## 过期重开记录

> 当某个豁免的"过期条件"触发时，从此表移除并转为 Sprint Backlog 活跃 Issue。

| ID | 原豁免日期 | 重开日期 | 触发条件 | 新 Sprint |
|----|-----------|----------|----------|:---------:|
| — | — | — | — | — |

---

## 统计

| 模块 | 豁免数 |
|------|:--:|
| Bridge | 0 |
| Core | 0 |
| Data | 1 |
| Infrastructure | 2 |
| UI | 0 |
| UDF | 0 |
| UI | 1 |
| **合计** | **5** |
