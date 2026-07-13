# CLAUDE.md

> 本项目的行为准则。灵感来自 [andrej-karpathy-skills](https://github.com/forrestchang/andrej-karpathy-skills)，专为本 BOM 插件项目定制。

---

## 四条核心准则

### 1. 先想后写 (Think Before Coding)

- **不确定就提问**。不要猜测业务规则——BOM 结构和差异计算有确定的领域语义。
- **说出来你做假设了**。"假设这条 BOM 路径不超过 5 级 → 代码按此编写，但如果用户数据实际是 6 级，这里会截断。"
- **主动呈现权衡**。"两种方案：A 用邻接表写 O(N) 的简单代码，B 用嵌套集写 O(1) 但更复杂的代码。V1.0 规模下 A 足够。"
- **发现架构偏离时停下来**。例如：发现自己在 WPF 线程直接调了 Excel COM → 停下，走 `ExcelThreadDispatcher`。

### 2. 简洁至上 (Simplicity First)

- **最少代码解决问题**。200 行的 Service 写成 50 行更好。
- **不为一成不变的场景建抽象层**。不要为"将来可能的图数据库迁移"写 Repository 抽象层——V1.0 用邻接表 + Dapper 就够了。
- **核心约束要尊重**：离线仅 ERP 同步暂停（本地读写始终可用）、Excel 2016 基准、线程隔离强制。不要因为"更好的用户体验"而越过这些边界。
- **自检**：一个资深 .NET 开发者看这段代码会觉得过度设计吗？如果是，简化。

### 3. 精准修改 (Surgical Changes)

- **只改该改的**。如果任务是"修复 BOM 展开的死锁"，不要顺带重构 `VarianceCalculator` 的命名约定。
- **匹配现有风格**。即使你更喜欢 `_camelCase`，如果周围代码用 `PascalCase`，保持一致。
- **发现无关的死代码/注释/格式问题时，提出来——不要擅自改**。在 review.md 中记录，让团队决定。
- **只清理因你的改动而变成垃圾的 import / 变量 / 函数**。

### 4. 目标驱动 (Goal-Driven Execution)

- **先定义验证方式，再开始写代码**。
- 将指令转化为可验证目标：

| 而不是 | 而是 |
|--------|------|
| "添加 BOM 展开缓存" | "BOMEXPAND('MAT-001') 第二次调用的耗时 < 首次的 10%，且结果一致。去验证。" |
| "修复线程 Bug" | "运行 threading 压力脚本连续 2h，0 个 RPC_E_WRONG_THREAD 异常。去验证。" |
| "重构 Repository" | "重构前后所有集成测试通过，且代码行数减少 30%+。去验证。" |

- **多步骤任务格式**：
  ```
  1. [写最小复现测试] → verify: 测试 FAILS（证明 Bug 存在）
  2. [写修复]           → verify: 测试 PASSES + 无回归
  3. [清理]             → verify: diff 只含相关改动
  ```

---

## 第五条准则：会话管理 (Session Hygiene)

> 上下文 = 资源。膨胀 = 劣化。长会话中 AI 会逐渐遗忘早期约束、混淆版本、产生幻觉。

### 何时自查

- **每完成一个独立功能点** — 对照 CLAUDE.md 四条准则自检
- **上下文超过 2 个 Sprint 的代码变更时** — 考虑总结当前状态、开新会话
- **反复纠正 AI 同一个错误时** — 这是幻觉信号，停下来写进 `exemptions.md` 或更新对应文档

### 防幻觉铁律

| 铁律 | 说明 |
|------|------|
| **不靠记忆引用文档** | 每次引用 `docs/` 或 `skills/` 中的内容时，先搜索/阅读确认，不凭印象 |
| **不确定 = 承认不确定** | 不要编造 BOM 业务规则；说"我需要在 spec 中确认"然后去查 |
| **写过的代码 = 读过的代码** | 不要假设自己知道某个文件内容——Read 它或 Grep 确认后再改 |
| **版本号是事实锚点** | 每个结论标注来源文档版本，防止误用过时信息 |

### 跨会话接力

```
上一个会话结束时 → 在回复末尾简述：
  ✅ 已完成: [具体交付物]
  🔜 下一步: [下一动作 + 涉及文件]
  ⚠️ 待决策: [阻塞项]
  📄 关键上下文: [后续会话必须知道的约束/假设]
```

---

## 信息组织：单源真理 (Single Source of Truth)

> **每个事实只在一处定义。其余各处用链接引用，绝不复制。**

| 规则 | 示例 |
|------|------|
| 架构定义 → 唯一在 `docs/specification.md` | plan.md 不重复画架构图，只写 `见 spec §2` |
| Sprint 排期 → 唯一在 `docs/plan.md` | spec 不写时间线，skill 不写 Sprint 归属 |
| 文件路由 → 唯一在 `docs/project-structure.md` | skill 不重复声明命名空间约定 |
| 代码范式 → 唯一在 `skills/*.md` | spec 不重复写完整代码示例 |
| 豁免决策 → 唯一在 `.claude/exemptions.md` | 不在 review.md 中重复登记 |
| 约束红线 → 定义在 `docs/specification.md` §1.2 | 其余文档只引用，不自行解释 |

### 违反信号

> 如果你发现同一个数字、规则、流程在两个文档中出现 → 这是腐化信号。**删除一处，改为链接。**

---

## 项目速查

### 我们在做什么

基于 **Excel-DNA** 的企业级 **BOM 管理与差异分析** Excel 插件。支持 10 万物料、100 万 BOM 节点。

### 架构（严格单向依赖）

```
BomAddIn (UI+Bridge+UDF, net472)
  → BomAddIn.Core (BLL+Variance, netstandard2.0)   ← 零外部依赖，纯 C#
    → BomAddIn.Data (DAL, netstandard2.0)           ← SQLite + DuckDB
      → BomAddIn.Infrastructure (Log/Security, netstandard2.0)
```

### 三条红线

> 定义在 [spec §1.2](docs/specification.md#12-v10-核心约束)。V1.0 离线仅限只读 → V1.1 已放宽为 ERP 同步暂停。

| 红线 | 含义 |
|------|------|
| 离线仅只读 → v1.1 放宽 | 本地 SQLite+DuckDB，离线可编辑，仅同步暂停 |
| Excel 2016 基准 | 365 动态数组是增强，不是依赖 |
| 线程隔离强制 | 所有 COM 调用必须经 `ExcelThreadDispatcher` |

### 任务路由表

> 按任务类型查找对应文档。**先读后写 — 确保不重复定义已有事实。**
>
> ⚠️ **Skill 自动触发**: `.claude/settings.local.json` 配置了 PreToolUse Hook——对 `.cs` 源文件执行 Edit/Write 前会自动检查相关 Skill 是否已读。参见 `.claude/hooks/check-skill.sh`。

| 我要做什么 | 应该读 | 应该改 |
|-----------|--------|--------|
| 新建一个程序集 / 添加 NuGet 包 | `docs/project-structure.md` §2-§3 | `docs/project-structure.md`（更新依赖图） |
| 新建一个 Service / Repository | `skills/excel-dna-di-startup.md` §2 | 对应 `.cs` 文件 + DI 注册 |
| 添加一个新 UDF | `docs/api-reference.md` + `skills/excel-udf-best-practices.md` | `docs/api-reference.md`（新函数）+ 新的 `src/BomAddIn/UDF/XxxFunctions.cs` |
| 修改 UDF 行为/签名 | `docs/api-reference.md`（当前合约） | `docs/api-reference.md` + 对应 UDF 代码 |
| 修改 BOM 数据表结构 | `docs/specification.md` §4 + `skills/bom-modeling-patterns.md` | `docs/specification.md`（表定义）+ 迁移脚本 |
| 在 WPF 仪表盘中加一个新面板 | `skills/excel-dna-wpf-dashboard.md` | 新的 View + ViewModel + 更新 `docs/specification.md` §7.3 |
| 修改离线/同步逻辑 | `skills/offline-first-architecture.md` | `BomAddIn.Data/Sync/` 或 `BomAddIn.Infrastructure/Network/` |
| 调整 Sprint 计划 / 排期 | `docs/plan.md` §2 | `docs/plan.md` |
| 写用户文档 / FAQ | `docs/user-manual.md` | `docs/user-manual.md` |
| 登记一个豁免问题 | `.claude/exemptions.md` | `.claude/exemptions.md` |
| 审查代码安全性 | `docs/specification.md` §11 | `.claude/review-reports/` |
| 排查线程 Bug | `skills/excel-dna-threading.md` §4（反模式表） | 代码 + `.claude/exemptions.md`（如需豁免） |
| 准备 Sprint 0 骨架 | `docs/project-structure.md` §10 | 新建 `.sln` + `.csproj` + `.dna` |
| 不确定该读什么 | `docs/CONTEXT.md` → 文档阅读顺序 | — |

### 不要做的事

- ❌ 不要直接 `new` 一个 Service —— 用 DI 容器
- ❌ 不要在 WPF / Timer / Task.Run 中直接调 Excel COM —— 用 `ExcelThreadDispatcher`
- ❌ 不要手动 `Marshal.ReleaseComObject` —— 让 GC 管
- ❌ 不要在 UDF 中缓存 `Application` 或 `Range` 引用
- ❌ 不要创建反向依赖（Core 引用 Data，Infrastructure 引用 Core）
- ❌ 不要为 "V2.0 可能需要" 写代码 —— 留扩展点就够了

---

## 工作流惯例

### 开始新功能时

> ⚠️ **铁律：Skill-first。先读 Skill，再写代码。不凭记忆编造实现方式。**

| 步骤 | 动作 | 门禁 |
|:--:|------|------|
| 1 | 确认 Sprint（`docs/plan.md` → §2） | — |
| 2 | **🛑 强制：读所有相关 skill**（见下方 Sprint↔Skill 映射表） | 未读 = 不许写代码 |
| 3 | 写验证标准 | 用 spec KPI 反推预期行为 |
| 4 | 实现 | 代码范式以 skill 为准，不以记忆为准 |

**Sprint ↔ Skill 映射**（代码范式以 skill 为唯一权威，spec 只定义"是什么"）：

| Sprint | 必须先读的 Skill |
|--------|-----------------|
| Sprint 0 (骨架) | `excel-dna-di-startup.md` §2-§5 |
| Sprint 1 (数据+登录) | `bom-modeling-patterns.md` §1-§2, `excel-dna-di-startup.md` §4 |
| Sprint 2 (权限+DAL) | `excel-dna-di-startup.md` §3, `offline-first-architecture.md` §3-§4 |
| Sprint 3 (BOM+差异+同步) | `bom-modeling-patterns.md` §3-§4, `offline-first-architecture.md` §5 |
| Sprint 4 (仪表盘+UDF) | `excel-dna-wpf-dashboard.md`, `excel-udf-best-practices.md` |
| Sprint 5 (审计+快照+性能) | `offline-first-architecture.md` §4 |
| Sprint 6 (测试+签名打包) | 所有 skill 的"自检清单"节

### 遇到 Bug 时
1. 写最小复现测试 → confirm: 测试 FAILS（Bug 存在）
2. 修 → confirm: 复现测试 PASSES + 已有测试无回归
3. **保留复现测试**（它现在是回归守卫——下次同样的 Bug 会被 CI 拦截）
4. 检查是否需要更新 spec / skill / review（Bug 暴露了文档缺口？技术模式缺失？）

### 提交前
- [ ] 所有新代码有对应的测试
- [ ] 无 WPF 线程直接碰 Excel COM
- [ ] 命名空间与文件夹一致
- [ ] 没动无关文件
- [ ] `dotnet build` 通过 + 测试通过
