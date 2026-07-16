# BomAddIn — 企业级 BOM 管理与差异分析 Excel 插件

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-4.7.2%2B%20%7C%20netstandard2.0-blue)](https://dotnet.microsoft.com/)
[![Excel-DNA](https://img.shields.io/badge/Excel--DNA-latest-green)](https://github.com/Excel-DNA/ExcelDna)

基于 **Excel-DNA** 的企业级物料清单（BOM）管理与多维差异分析插件。直接在 Excel 中展开 BOM、比对版本差异、监控价格/库存波动、查看可视化仪表盘——无需离开工作表。

**目标规模**: 10 万级物料 · 100 万级 BOM 节点 · 5 级+ 层级深度

---

## 快速开始

### 用户

```
1. 解压 BomAddIn.zip → 双击 BomAddIn-AddIn.xll
2. Excel Ribbon 出现「BOM Suite」→ 登录
3. 在任意单元格输入 =BOMEXPAND("物料编码")
```

> 📖 [用户手册](./docs/user-manual.md) — 安装、功能指南、故障排查、速查卡  
> 📋 [API 参考](./docs/api-reference.md) — 8 个 UDF 的完整语法与示例

### 开发者

```bash
git clone <repo-url>
# 打开 BomAddIn.sln → 生成 → 启动 Excel 调试
```

> 🏗️ [项目结构](./docs/project-structure.md) — 程序集清单、依赖图、命名空间、Sprint 0 骨架清单  
> 📐 [技术规格](./docs/reference/specification.md) — 架构、数据模型、引擎算法、安全策略  
> 📅 [实施计划](./docs/reference/plan.md) — Sprint 路线图、风险登记册、CI/CD

---

## 功能概览

| 功能 | 说明 | 入口 |
|------|------|------|
| **BOM 展开** | 展开任意物料的多层级 BOM，支持时间切片和版本过滤 | `=BOMEXPAND("编码")` |
| **成本汇总** | 递归计算 BOM 全树汇总成本 | `=BOMCOST("编码")` |
| **差异分析** | 跨版本/时间/供应商维度比对 BOM 变化 | `=VARIANCECHECK()` + 仪表盘 |
| **价格与库存** | 实时查询物料价格、库存量、订单状态 | `=PRICELOOKUP()` `=INVENTORYQTY()` |
| **预警监控** | 价格波动 >10%、库存低于安全线自动告警 | `=ALERTCHECK()` + 仪表盘 |
| **WPF 仪表盘** | 独立窗口：KPI 概览、差异趋势图、预警列表、BOM 结构树 | Ribbon → 仪表盘 |
| **离线只读** | 网络断开自动切换本地 SQLite 缓存，恢复后自动同步 | 透明切换 + 状态水印 |
| **ERP 同步** | 适配器模式对接 ERP，Polly 重试，Excel 导入备用通道 | 定时自动 + 手动触发 |
| **审计与合规** | 全量增删改记录、AES-256 加密、BCrypt 认证、GDPR 就绪 | AOP 拦截透明记录 |

---

## 架构

```
BomAddIn (UI + Bridge + UDF, net472)
  → BomAddIn.Core (BLL + Variance Engine, netstandard2.0)  ← 零外部依赖
    → BomAddIn.Data (DAL + Sync + Cache, netstandard2.0)
      → BomAddIn.Infrastructure (Log/Security/Audit, netstandard2.0)
```

**关键约束**: 离线 ERP 同步暂停（本地读写可用） · Excel 2016 基准 · 线程隔离强制

---

## 文档导航

| 我想... | 去看 |
|---------|------|
| 新成员快速了解项目 | [CONTEXT.md](./docs/CONTEXT.md) |
| 安装和使用插件 | [user-manual.md](./docs/user-manual.md) |
| 查函数语法和示例 | [api-reference.md](./docs/api-reference.md) |
| 了解技术架构 | [specification.md](./docs/reference/specification.md) |
| 看项目排期和风险 | [plan.md](./docs/reference/plan.md) |
| 找代码该放哪个文件夹 | [project-structure.md](./docs/project-structure.md) |
| 理解设计决策的由来 | [review.md](./docs/reference/review.md) |
| 了解开发模式和陷阱 | [skills/](./skills/) |
| 登记或查看豁免问题 | [.claude/exemptions.md](./.claude/exemptions.md) |

---

## 技术栈

| 层 | 技术 |
|----|------|
| Excel 集成 | Excel-DNA · COM Interop · WPF · WinForms |
| 业务逻辑 | C# · FluentValidation · LINQ |
| 数据访问 | Dapper · SQLite (CRUD) + DuckDB (分析) · DbUp |
| 基础设施 | NLog · Polly · BCrypt · DPAPI · BenchmarkDotNet |
| 测试 | xUnit · Moq · TestContainers |
| CI/CD | GitHub Actions / Azure DevOps · SonarQube · ExcelDnaPack |

---

## 贡献

本项目遵循 [CLAUDE.md](./CLAUDE.md) 中定义的行为准则（灵感来自 [andrej-karpathy-skills](https://github.com/forrestchang/andrej-karpathy-skills)）：

1. **先想后写** — 不确定就提问，不隐藏假设
2. **简洁至上** — 最少代码解决问题，尊重核心约束
3. **精准修改** — 只改该改的，匹配现有风格
4. **目标驱动** — 先定义验证标准，再写代码
5. **会话管理** — 防止上下文膨胀和幻觉

提交前确保：新代码有测试 · 无跨线程 COM 调用 · 命名空间与文件夹一致

---

## 许可

MIT © 2026 CostSuite
