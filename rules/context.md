# 项目上下文 (Project Context)

> **受众**: 新加入团队的开发者  
> **阅读时间**: 5 分钟  
> **读完下一步**: [project-structure.md](./project-structure.md)（找代码放哪）→ [specification.md](./reference/specification.md)（理解架构）  

---

## 我们在解决什么问题

制造业企业的 BOM 数据分散在 ERP、Excel 工作表、邮件附件中。工程师和采购员需要：

- 在 Excel 中**展开多层级 BOM**，而不是登录 ERP 点十几层菜单
- **比较两个版本**的 BOM 差异（上次采购 vs 这次、设计变更前 vs 后）
- 看到**价格和库存的波动**，而不是月底对账才发现
- **断网时也能查数据**（工厂车间网络不稳定）

当前流程：导出 CSV → VLOOKUP 手工比对 → 容易出错 → 追溯到原始数据很痛苦。

**一句话**: 把 BOM 管理和差异分析的能力直接嵌入 Excel，用户无需切换工具。

---

## 用户是谁

| 角色 | 典型场景 | 最常用功能 |
|------|---------|-----------|
| BOM 管理员 | 维护产品 BOM 结构，发布新版本 | `BOMEXPAND`、版本管理 |
| 采购员 | 查报价、比价、看价格趋势 | `PRICELOOKUP`、`VARIANCECHECK`（价格维度） |
| 成本分析师 | 核算产品成本、预算 vs 实际偏差 | `BOMCOST`、`VARIANCECHECK`（预算维度） |
| 生产计划员 | 确认库存能否满足生产订单 | `INVENTORYQTY`、`ORDERSTATUS` |
| 管理层 | 看 KPI 仪表盘，审批 BOM 变更 | WPF 仪表盘 |

---

## 技术边界（为什么这样选）

### 为什么是 Excel-DNA 而不是 VSTO 或 Office.js？

| 选项 | 为什么不选 |
|------|-----------|
| VSTO | 需要管理员权限安装、部署复杂、版本绑定 |
| Office.js | 无法访问本地文件系统、无法做离线 SQLite、性能不足以处理百万级节点 |
| **Excel-DNA** ✅ | 零安装（双击 .xll 即可）、原生 C# 性能、可访问本地 SQLite、支持 WPF |

代价：必须手动管理 COM 线程（`ExcelThreadDispatcher`）——这是整个项目最大的技术风险。

### 三条红线从哪来

| 红线 | 来源 |
|------|------|
| 离线仅只读 → **v1.1 放宽** | 离线双向同步的冲突解决是另一个项目的工作量。v1.1 采用 SQLite 本地主库后，离线编辑已支持，仅 ERP 同步暂停。V2.0 目标：双向同步 + 冲突解决。 |
| Excel 2016 基准 | 客户环境还有大量 Office 2016。365 动态数组是加分项，不能成为依赖 |
| 线程隔离强制 | Excel-DNA 最常见的崩溃原因。Sprint 0 就验证，不通过则项目暂停 |

---

## 五句话了解技术栈

```
Excel 宿主 ← Excel-DNA (C# .dll → .xll)
业务逻辑 ← C# netstandard2.0（纯代码，可独立单测）
数据访问 ← Dapper + SQLite（本地主库）+ DuckDB（分析引擎）
数据同步 ← 内置指数退避重试 + 适配器模式对接 ERP
前端     ← WPF 仪表盘（独立窗口）+ Excel UDF（公式栏）
```

---

## 文档阅读顺序

```
新加入团队：
  1. 本文档 (CONTEXT.md)         ← 你现在在这里
  2. project-structure.md        ← 代码在哪、怎么组织
  3. reference/specification.md            ← 架构和数据模型
  4. reference/plan.md                     ← Sprint 和风险
  5. skills/                     ← 写代码前读对应的 skill

已有上下文，需要写代码：
  agents.md → 任务路由表         ← “我要做 X，该读哪个文件”

审查与质量：
  .claude/reports/code-review-2026-07-13.md  ← 最近一次全面深度审查报告（77 项发现）
```

---

## 关键术语

| 术语 | 含义 |
|------|------|
| BOM (Bill of Materials) | 物料清单 — 一个产品由哪些物料组成、各用多少 |
| BOM 展开 | 从顶层物料递归查找所有子物料，展平为列表 |
| BOM 版本 (BomVersion) | 同一产品的 BOM 可以有多个版本：Draft → Released → Obsolete |
| 差异分析 (Variance) | 比较两个 BOM 版本/时间点的物料、价格、库存变化 |
| xBOM | 同一 BOM 数据的不同视图 — EBOM（设计）、MBOM（制造）、CBOM（成本） |
| 邻接表 | BOM 的存储模型：每行记录 `ParentMaterialId → ChildMaterialId` 的关系 |
| COM 封送 (Marshalling) | 将 WPF 线程的操作"快递"到 Excel 主线程执行 |

---

## 常见问题（新成员篇）

**Q: 为什么不直接用 Clean Architecture？**

A: 原始方案定义了六层架构。为了降低团队认知成本，当前文档保持 BLL/DAL/Engine 等原术语。可在 Sprint 中渐进式重构。

**Q: Bridge 层为什么和 UI 层在同一个程序集？**

A: Excel-DNA 要求所有使用其 API 的代码在同一 AppDomain。拆分出来只会增加跨程序集封送复杂度。

**Q: 测试为什么用 `net472` 而不是 `net8.0`？**

A: 线程安全测试需要加载真实的 Excel-DNA 和 COM API。单元测试可以用 `net8.0`，但统一为 `net472` 简化 CI。

**Q: 我可以直接改 `reference/review.md` 吗？**

A: 不可以。这是 📸 只读快照——记录的是某个时间点的评审结论，不是持续更新的文档。

---

> 📋 项目入口：[README.md](../README.md)（用户视角）  
> 🤖 AI 协作：[agents.md](../agents.md)（AI 行为准则）  
> 📐 全文档导航：[project-structure.md §2.1](./project-structure.md#21-文档分工5s单源真理)
