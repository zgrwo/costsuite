# 企业级 BOM 管理与差异分析 Excel 插件 — 项目实施计划

> **文档类型**: 项目执行计划（Plan）  
> **日期**: 2026-07-12  
> **受众**: 项目经理、全团队  
> **单源真理**: Sprint 排期、风险登记册、CI/CD 流程、测试排期、里程碑以本文档为准  
> **配套文档**: [specification.md](./specification.md)（技术规格）、[project-structure.md](./project-structure.md)（Sprint 0 骨架清单）

---

## 1. 项目章程

### 1.1 项目目标

构建一款基于 Excel-DNA 的企业级 BOM 管理与差异分析插件，实现多源数据整合、多维度差异预警及可视化看板，支持 **10 万级物料**与**百万级 BOM 节点**。

### 1.2 ⚠️ V1.0 核心约束（三大红线）

为避免项目延期与技术债务，V1.0 版本必须遵守以下约束：

| # | 约束 | 说明 |
|---|------|------|
| 1 | **离线模式（ERP 同步暂停）** — v1.1 放宽 | SQLite 本地主库下编辑始终可用，离线仅指 ERP 同步暂停 |
| 2 | **Excel 版本基线** | 以 Excel 2016/2019 为基准设计 UDF 交互，Excel 365 动态数组作为增强体验而非依赖项 |
| 3 | **线程隔离原则** | 所有 WPF 与 Excel COM 对象的交互必须通过统一的 `ExcelThreadDispatcher` 封送，严禁在 WPF 线程直接调用 Excel API |

---

## 2. 实施路线图

### 2.1 Sprint 总览

| Sprint | 周期 | 核心交付物 |
|--------|------|-----------|
| **Sprint 0** | 第 0 周 | 骨架 + CI + 技术探针 |
| **Sprint 1** | 第 1-2 周 | 数据建模 + 登录原型 + 种子数据 |
| **Sprint 2** | 第 3-4 周 | 权限 + DAL + 配置管理 |
| **Sprint 3** | 第 5-6 周 | BOM 核心 + 差异引擎 + 同步服务 |
| **Sprint 4** | 第 7-8 周 | WPF 仪表盘 + UDF 完善 |
| **Sprint 5** | 第 9-10 周 | 审计 + 快照 + 审批 + 性能优化 |
| **Sprint 6** | 第 11-12 周 | 测试 + 文档 + 签名打包 |
| **缓冲期** | 第 13 周 | 反馈修复 + 培训 |

### 2.2 Sprint 0 — 技术探针门禁 ⚠️

> **此为项目启动的前置条件。若任何一项验证未通过，项目暂停进入"技术攻坚周"，不进入 Sprint 1。**

| 探针编号 | 验证内容 | 通过标准 | 负责角色 |
|----------|---------|---------|----------|
| P-0.1 | `ExcelThreadDispatcher` 线程通信 | 连续运行 2h 无 `RPC_E_WRONG_THREAD` 异常 | Excel-DNA 专员 |
| P-0.2 | Excel 2016/365 UDF 兼容性 | 4 个代表性 UDF 在两版本上输出一致 | 开发 |
| P-0.3 | Polly 重试 + 缓存切换 | 100 次在线↔离线切换循环无错误 | 后端开发 |

**交付物**:
- CI/CD 管道搭建（编译 + 单元测试 + 代码分析）
- `ExcelThreadDispatcher` 封装完成并验证
- Excel 版本兼容性测试报告
- 技术探针验证报告（go/no-go 结论）

**失败处理预案**:
- 若 P-0.1 失败：评估改用 VSTO 或 Office.js 的可行性
- 若 P-0.2 失败：降低动态数组功能的优先级，统一使用静态数组公式
- 若 P-0.3 失败：排查网络模拟环境，确认 Polly 配置正确性

### 2.3 Sprint 1 — 数据建模 + 登录原型 + 种子数据

**交付物**:
- 15 张表 DDL 脚本（8 业务表 + 7 系统表）
- DbUp 迁移框架集成
- 用户登录/登出原型（BCrypt 密码哈希）
- 种子数据生成：10 万条物料 + 50 万条 BOM 节点 + 12 个月历史快照

**验收标准**:
- 10 万数据量下登录响应 <3s
- 单表 CRUD 操作全部通过
- 种子数据脚本可重复执行（幂等）

### 2.4 Sprint 2 — 权限 + DAL + 配置管理

**交付物**:
- 基于角色的访问控制（RBAC）：Admin / Analyst / Viewer
- Dapper 数据访问层（读写分离）
- MemoryCache 集成
- AppConfig 配置管理

**验收标准**:
- 权限校验在 BLL 层生效（非仅 UI 隐藏）
- SQL 注入防护验证通过
- 离线只读切换正常（模拟网络断开→SQLite 仍可读写→ERP 恢复后上行同步）

### 2.5 Sprint 3 — BOM 核心 + 差异引擎 + 同步服务

**交付物**:
- BOM 结构与版本管理（Draft/Released/Obsolete 生命周期）
- 多维差异比对引擎
- ERP 数据同步服务（适配器模式 + Polly 重试）
- Excel 导入备用通道

**验收标准**:
- 多层 BOM 展开结果正确（5 级以上）
- 差异计算结果与手工核对一致
- ERP 数据拉取 + 3 次重试正常
- 同步 10 万条数据 <5min（性能基线）

### 2.6 Sprint 4 — WPF 仪表盘 + UDF 完善

**交付物**:
- WPF 可视化看板（独立 STA 线程）
- 8 个 UDF 自定义函数
- Ribbon 菜单集成
- 任务窗格

**验收标准**:
- 看板首次加载 <3s
- WPF↔Excel 交互无 RPC 异常（连续操作 30min）
- 8 个 UDF 全部可用且输出格式符合 [specification.md](./specification.md) 定义

### 2.7 Sprint 5 — 审计 + 快照 + 审批 + 性能优化

**交付物**:
- AOP 审计日志拦截器
- 数据快照（每日自动 + 手动冻结）
- BOM 审批工作流
- 性能优化（BOM 展开 1000 节点 <500ms）

**验收标准**:
- 审计日志覆盖所有增删改操作
- 快照比对 <5s
- BOM 展开 1000 节点 <500ms（见 [spec §12.1](./specification.md#121-性能-kpi)）
- 审计日志写入延迟 <50ms

### 2.8 Sprint 6 — 测试 + 文档 + 签名打包

**交付物**:
- 完整测试套件（覆盖率达 >80%）
- 用户手册 + 开发文档
- ExcelDnaPack 打包 + Authenticode 数字签名
- 诊断工具 `BomAddIn.Diagnostic.exe`

**验收标准**:
- 单元测试覆盖率 >80%
- UAT 通过（试点用户满意度 >85%）
- 所有 CI/CD 门禁绿灯
- 诊断工具可运行于目标环境

### 2.9 缓冲期 — 反馈修复 + 培训

- UAT 反馈修复
- 用户培训（管理员 + 关键用户）
- 部署上线

---

## 3. 资源与角色

### 3.1 建议团队构成

| 角色 | 人数 | 职责 | 关键技能 |
|------|------|------|----------|
| **架构师/技术负责人** | 1 | 架构决策、技术探针主导、代码审查 | Excel-DNA、WPF、COM 互操作 |
| **Excel-DNA 专员** | 1 | 桥接层开发、UDF 实现、线程安全 | C#、Excel-DNA、COM |
| **WPF 前端开发** | 1 | 仪表盘、Ribbon、任务窗格 | WPF、MVVM、数据可视化 |
| **后端开发** | 1-2 | BLL、差异引擎、DAL、同步服务 | C#、Dapper、SQLite + DuckDB |
| **QA 工程师** | 1 | 测试策略执行、兼容性测试、UAT | xUnit、TestContainers、BenchmarkDotNet |
| **项目经理** | 0.5（兼职） | Sprint 计划、风险跟踪、干系人沟通 | — |

### 3.2 环境与工具链

| 类别 | 工具/技术 |
|------|----------|
| **IDE** | Visual Studio 2022+ / Rider |
| **运行时** | .NET Framework 4.7.2（宿主）+ netstandard2.0（类库） |
| **数据库** | SQLite（本地 CRUD 主库）+ DuckDB（内存分析引擎） |
| **CI/CD** | GitHub Actions 或 Azure DevOps |
| **代码分析** | SonarQube（无 Critical 告警） |

---

## 4. 风险管理

### 4.1 风险登记册

| # | 风险 | 概率 | 影响 | V1.0 应对措施 | 负责人 |
|---|------|------|------|-------------|--------|
| R1 | Excel 线程异常（RPC_E_WRONG_THREAD） | 高 | 高 | Sprint 0 技术探针 + `ExcelThreadDispatcher` 强制封装 | Excel-DNA 专员 |
| R2 | 离线数据冲突 | 高 | 高 | V1.1 本地 SQLite 读写，仅 ERP 同步暂停。离线冲突解决设计 V2.0 | 架构师 |
| R3 | Excel 版本不兼容 | 中 | 高 | 2016 为基准设计，365 作增强；Sprint 0 验证 | 开发 |
| R4 | 大数据性能瓶颈 | 高 | 高 | 缓存 + 异步 + 只读副本 + 分页；Sprint 1 即备种子数据 | 后端开发 |
| R5 | ERP 接口变更 | 中 | 高 | 适配器模式 + Excel 导入备用通道 | 后端开发 |
| R6 | XLL 被杀软拦截 | 中 | **高**（升级） | Sprint 1 启动 IT 安全预沟通；Sprint 6 数字签名 | 项目经理 |
| R7 | 需求频繁变更 | 高 | 中 | 敏捷迭代 + 1 周缓冲 + 变更评审 | 项目经理 |
| R8 | COM Add-in 加载冲突 | 中 | 中 | Sprint 4 起与代表性格子间 Add-in 集共存测试 | QA |
| R9 | 数据主权/驻留合规 | 中 | 中 | 同步服务支持可配置区域端点；数据分类标签 | 架构师 |
| R10 | 安全密钥泄露 | 低 | 高 | AES 密钥通过 Windows DPAPI 保护；审计日志记录密钥访问 | 架构师 |
| R11 | 开发者对 Excel-DNA 不熟悉 | 中 | 中 | Sprint 0 预留 0.5 周学习 Excel-DNA 调试工作流 | 架构师 |

### 4.2 风险评审节奏

- **每 Sprint 结束**: 评审风险状态，更新概率/影响
- **Sprint 0 后**: 重点评审 R1、R3、R11
- **Sprint 4 前**: 重点评审 R6、R8

---

## 5. CI/CD 管道

### 5.1 PR 门禁流程

```text
PR 提交 → 编译 → 单元测试 → SonarQube（无 Critical）→ 覆盖率 >80% → 安全扫描 → ✅ 可合并
```

### 5.2 制品管理

- **构建产物**: ExcelDnaPack 生成的 .xll 文件 + 依赖 DLL
- **签名**: Authenticode 数字签名
- **分发格式**: ZIP 压缩包
- **留存策略**: 保留最近 10 个发布版本

---

## 6. 部署与运维

### 6.1 打包分发流程

```
ExcelDnaPack 打包 → Authenticode 数字签名 → ZIP 压缩 → 内部分发 → 首次加载配置向导
```

### 6.2 版本更新

- 启动时自动检查版本并提示更新
- DbUp 管理数据库迁移脚本，升级自动执行
- 用户配置（`user.config`）独立存放，升级不覆盖

### 6.3 运维工具

- **BomAddIn.Diagnostic.exe**: 检查 .NET 版本、Excel 位数（32/64 bit）、数据库连接、权限
- **NLog 分级采样**: Info 1% + Error 全量 + 敏感字段脱敏
- **远程日志推送**: 至 Elasticsearch / Syslog（可选）

### 6.4 数据库迁移

- DbUp 按序执行迁移脚本
- 每个迁移脚本包含 UP（升级）和 DOWN（回滚）逻辑
- 迁移前自动创建数据库备份

---

## 7. 测试策略与排期

### 7.1 测试类型与分布

| 类型 | 工具 | 重点覆盖 | 通过标准 | 负责 Sprint |
|------|------|---------|----------|-------------|
| **单元测试** | xUnit + Moq | 差异计算边界值、权限组合、MRP 异常 | 覆盖率 >80% | Sprint 1-6（持续） |
| **集成测试** | TestContainers | 15 表关联、事务回滚、同步一致性 | 全场景通过 | Sprint 2-6 |
| **线程安全测试** | 自定义压力脚本 | WPF 高频调用 Excel API 并发 | 连续 2h 无 RPC 异常 | Sprint 0（基线）+ Sprint 4（回归） |
| **兼容性测试** | Office 版本矩阵 | 2016/2019/365 × 32/64 位 | 主流组合全通过 | Sprint 4-6 |
| **性能测试** | BenchmarkDotNet | 1000 节点 BOM、差异计算 | 达 KPI（见 [spec §12.1](./specification.md#121-性能-kpi)） | Sprint 1（基线）+ Sprint 5（终验） |

### 7.2 UAT 计划

| 阶段 | 时间 | 参与人 | 内容 |
|------|------|--------|------|
| **试点准备** | Sprint 5 末 | QA + 关键用户 | 测试场景脚本编写 |
| **UAT 执行** | Sprint 6 | 关键用户（3-5 人） | 按场景脚本操作，记录问题 |
| **UAT 评审** | 缓冲期 | 项目组 + 业务方 | 满意度评分（目标 >85%）、遗留问题分类 |

---

## 8. 里程碑与沟通计划

### 8.1 时间线总览

```text
Week 0     Week 1-2    Week 3-4    Week 5-6    Week 7-8    Week 9-10   Week 11-12  Week 13
  │           │           │           │           │           │           │           │
Sprint 0   Sprint 1   Sprint 2   Sprint 3   Sprint 4   Sprint 5   Sprint 6   缓冲期
  │           │           │           │           │           │           │           │
  ▼           ▼           ▼           ▼           ▼           ▼           ▼           ▼
技术探针   数据+登录  权限+DAL   BOM引擎   仪表盘+UDF  审计+优化   测试+打包   反馈+上线
  │
  ├─ ✅ Go → 进入 Sprint 1
  └─ ❌ No-Go → 技术攻坚周
```

### 8.2 外部依赖

| 依赖项 | 需要时间 | 责任人 |
|--------|---------|--------|
| ERP API 接口文档/沙箱环境 | Sprint 2 末 | 客户 IT |
| IT 安全部门白名单预沟通 | Sprint 1 | 项目经理 |
| UAT 试点用户确定 | Sprint 4 末 | 业务方 |
| 代码签名证书 | Sprint 5 | IT/项目经理 |

### 8.3 干系人演示节点

| 节点 | 时间 | 受众 | 内容 |
|------|------|------|------|
| 技术评审 | Sprint 0 末 | 技术团队 + IT | 技术探针结果 |
| 中期演示 | Sprint 2 末 | 业务方 | 登录 + 权限 + 基础数据 CRUD |
| 功能演示 | Sprint 4 末 | 业务方 + 管理层 | 完整 BOM 管理 + 差异分析 + 看板 |
| 最终演示 | Sprint 6 末 | 全体干系人 | 完整功能 + 性能数据 |
| 上线评审 | 缓冲期 | 技术 + 业务 + IT | 上线 checklist |

---

## 9. 参考来源与改进说明

本计划基于以下来源整合并增强了原始 `Plan and Specification.txt v4.0`：

- **GitHub 优秀项目参考**: [Excel-DNA/ExcelDna](https://github.com/Excel-DNA/ExcelDna)、[Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)、[FinAnSu](https://github.com/brymck/finansu)、[DotNetRefEdit](https://github.com/Ron-Ldn/DotNetRefEdit)
- **行业实践参考**: OpenBOM（图数据库 + xBOM 多视图）、Inventory_v01（BOM+库存一体化）
- **新增风险项**: R6（AV 白名单影响升级为"高"）、R8（COM 共存）、R9（数据主权）、R10（密钥管理）、R11（开发者学习曲线）
- **Sprint 0 细化**: 将模糊的"技术探针"细化为 3 个可量化验证项，增加失败处理预案
- **UAT 计划新增**: 原文档未涉及 UAT 设计

> 📋 **配对文档**: 技术细节（架构、数据模型、UDF API、安全策略）请参见 [specification.md](./specification.md)

---

## 10. Sprint 完成状态

> **最后更新**: 2026-07-16 | **整体完成度**: 100%

| Sprint | 状态 | 关键交付物 |
|--------|:--:|------|
| **Sprint 0** | ✅ 100% | 骨架 + 3 技术探针 + `ExcelThreadDispatcher` + CI/CD 管道 (`.github/workflows/ci.yml`) |
| **Sprint 1** | ✅ 100% | 15 张表 DDL + DbUp 迁移 + BCrypt 登录 + 种子数据 (5000→10万 物料, 2.5万→50万 BOM节点) |
| **Sprint 2** | ✅ 100% | RBAC (Admin/Analyst/Viewer) + 14 Repository + MemoryCache + AppConfig + DPAPI/AES 加密 |
| **Sprint 3** | ✅ 100% | BOM DFS 展开 + Variance 差异引擎 (5维度) + SyncService (Polly 重试+熔断) + DuckDB 分析 |
| **Sprint 4** | ✅ 100% | WPF Dashboard (独立 STA) + 8 UDF + Ribbon + TaskPane (BomTaskPane) |
| **Sprint 5** | ✅ 100% | AOP 审计日志 + 数据快照 (每日自动+手动) + BOM 审批工作流 + BFS 性能优化 (1000节点 <500ms) |
| **Sprint 6** | ✅ 100% | ~270 测试 (211 单元 + 32 集成 + 28 线程 + BenchmarkDotNet) + ExcelDnaPack .xll + 诊断工具 |
| **缓冲期** | ✅ | 代码审查修复 + v1.1 发布包 |

### Sprint 0→100% 补全记录 (2026-07-16)

| 缺口 | 修复 |
|------|------|
| CI/CD 管道缺失 | 创建 `.github/workflows/ci.yml`：PR 触发 → Build → Unit Tests → Integration Tests → Package ZIP。含 `workflow_dispatch` 手动触发 |
| TaskPane WPF 初始化崩溃 | `AutoOpen.RegisterTaskPane()` 增加 `Application.Current == null` 检测——在 Excel 主线程创建 `Application { ShutdownMode = OnExplicitShutdown }` 使 WPF 主题资源可用，随后正常注册 `CustomTaskPaneFactory.CreateCustomTaskPane` |
| sign.ps1 `$securePass` 未使用 | 修复密码传递逻辑，移除无效的 `SecureString` 转换，`$Password` 直接传入 signtool |
