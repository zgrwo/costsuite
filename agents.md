# agents.md — BomAddIn (costsuite) 项目宪法

> 企业级 BOM 管理与差异分析 Excel 插件：Excel-DNA + C#，目标 10 万级物料 / 100 万级节点。
> 本文件面向 AI 编程助手，编码细节按需加载 Skill。

## 元数据

- **项目名**：BomAddIn (costsuite)
- **GitHub**：https://github.com/zgrwo/costsuite
- **语言**：C#（net472 + netstandard2.0）
- **数字唯一基准**：`rules/api-reference.md` — 8 个 UDF 签名以此为准
- **SSOT**：每个事实只在一处定义，其余仅链接引用

## 四条核心准则

### 1. 先想后写 (Think Before Coding)

- **不确定就提问**。不要猜测业务规则——去查 specification。
- **说出来你做假设了**。
- **发现架构偏离时停下来**。例如：发现自己在 Task.Run 中操作 Range → 停下，走 QueueAsMacro。

### 2. 简洁至上 (Simplicity First)

- **最少代码解决问题**。
- **不为一成不变的场景建抽象层**。原型不需要 MediatR/Polly/AES-256。
- **自检**：一个资深开发者看这段代码会觉得过度设计吗？

### 3. 精准修改 (Surgical Changes)

- **只改该改的**。不要顺带重构无关代码。
- **匹配现有风格**。
- **发现无关问题时提出来，不擅自改**。

### 4. 目标驱动 (Goal-Driven Execution)

- **先定义验证方式，再开始写代码**。

| 而不是 | 而是 |
|--------|------|
| "重构 Repository" | "重构前后所有集成测试通过，且代码行数减少 30%+。去验证。" |
| "修复线程 Bug" | "压力测试 30min，0 个线程异常。去验证。" |

## 架构分层

```
┌─────────────────────────────────────────┐
│  UI 层 (Ribbon + TaskPane + WPF)        │  ← 合并原 UI + Bridge
│  ExcelThreadDispatcher 仍保留           │
├─────────────────────────────────────────┤
│  Service 层 (BomService + SyncService)  │  ← 业务编排，有状态/事务
├─────────────────────────────────────────┤
│  Engine 层 (VarianceCalculator)         │  ← 纯计算，零依赖，可独立测试
├─────────────────────────────────────────┤
│  Data 层 (SQLite + DuckDB)             │  ← 合并原 DAL + Infrastructure
│  Dapper + 连接管理                      │
└─────────────────────────────────────────┘
```

- ✅ Engine 层零依赖（纯计算可独立单元测试）
- ✅ 离线优先：网络断开自动切换本地 SQLite
- ❌ 禁止跨线程 COM 调用
- ❌ 命名空间必须与文件夹一致

## 仓库目录树

> 路由地图：所有文件路径均以此为基准。详细结构见 [project-structure.md](rules/project-structure.md)。

```
costsuite/
├── src/                              # 源码（BomAddIn / Core / Data / Infrastructure）
├── tests/                            # xUnit 单元/集成/线程/性能测试
├── tools/                            # 独立工具（诊断）
├── database/                         # 数据库文件 (dev/prod)
├── build/                            # 构建与打包脚本
├── template/                         # 种子数据 CSV 模板
├── rules/                            # 规范文档
├── .qoder/skills/                    # Skill 定义（Qoder 运行时可发现）
├── skills/                           # Skill 源文件（人类可读副本）
├── logs/                             # 运行日志（gitignored）
├── .github/workflows/                # CI/CD pipeline
├── BomAddIn.sln                      # 解决方案文件
├── Directory.Build.props             # 共享 MSBuild 属性
├── .editorconfig                     # 代码风格统一
├── LICENSE                           # MIT 许可证
├── agents.md                         # 本文件
└── readme.md                         # 用户向功能指南
```

## 技能加载

修改代码前**必须**加载对应 Skill：

| 范围 | Skill 文件 | 内容 |
| :--- | :--- | :--- |
| BOM 建模 / 差异计算 | `.qoder/skills/bom-modeling-patterns/SKILL.md` | BOM 递归展开、差异算法、CTE 模式 |
| Excel-DNA 启动 / DI | `.qoder/skills/excel-dna-di-startup/SKILL.md` | 加载顺序、依赖注入、ExplicitExports |
| 线程 / COM 安全 | `.qoder/skills/excel-dna-threading/SKILL.md` | QueueAsMacro、STA 约束、Dispatcher |
| WPF 仪表盘 | `.qoder/skills/excel-dna-wpf-dashboard/SKILL.md` | TaskPane 集成、数据绑定 |
| UDF 编写 | `.qoder/skills/excel-udf-best-practices/SKILL.md` | 参数/返回值规范、错误处理 |
| 离线架构 | `.qoder/skills/offline-first-architecture/SKILL.md` | SQLite 缓存、同步策略、降级 |

### 专家 Skill（重构生命周期）

| 阶段 | Skill | 触发时机 |
|------|-------|----------|
| 决策前 | `.qoder/skills/architecture-reviewer/SKILL.md` | 新增组件/层级/依赖前 |
| 执行中 | `.qoder/skills/refactoring-guardian/SKILL.md` | 每个 Phase 开始/结束时 |
| 执行后 | `.qoder/skills/project-plan-review/SKILL.md` | 里程碑复盘/规划评审时 |

## 红线规则

### 1. 线程安全

- 🔴 Excel COM 对象只能在创建线程访问
- 🔴 后台任务通过 `QueueAsMacro` 回调 UI 线程
- 🔴 禁止在 Task.Run 中操作 Range/Worksheet

### 2. 数据层

- SQLite 用于 CRUD（轻量、离线）
- DuckDB 用于分析查询（列式、聚合）
- 迁移使用 System.Data.SQLite runner（非 DbUp）
- DEV/PROD 数据库路径本地化

### 3. BOM 引擎

- CTE 递归展开必须 BFS 迭代 + 全局去重（防路径爆炸）
- 层级深度 5+ 级，节点 100 万级
- 时间切片 + 版本过滤

### 4. 安全

- BCrypt 认证 + DPAPI 加密
- AES-256 字段加密（代码就绪，V2.0 启用）
- AuditService 关键操作审计
- ERP 同步内置指数退避重试

### 5. Excel-DNA 兼容

- ExplicitExports=true（BCrypt 等纯依赖用 Reference）
- net472 基准（Excel 2019+ 兼容）
- UDF 参数全部 object

## 技术栈

| 层 | 技术 |
|---|---|
| Excel 集成 | Excel-DNA · COM Interop · WPF · WinForms |
| 业务逻辑 | C# · FluentValidation · LINQ |
| 数据访问 | Dapper · SQLite + DuckDB · System.Data.SQLite |
| 基础设施 | BCrypt · DPAPI · BenchmarkDotNet · AppLogger |
| 测试 | xUnit · Moq |
| CI/CD | GitHub Actions |

## 构建与测试

| 场景 | 命令 |
| :--- | :--- |
| 日常构建 | `dotnet build` |
| 运行测试 | `dotnet test` |
| 分发构建 | `dotnet build -c Release` + ExcelDnaPack |

## 启动与诊断

### 非交互式启动

```powershell
# 1. 还原依赖
dotnet restore BomAddIn.sln

# 2. 构建
dotnet build BomAddIn.sln

# 3. 运行测试
dotnet test BomAddIn.sln --no-build
```

### 环境诊断 (Doctor)

```powershell
# 检查运行时环境
node -e "console.log('Node:', process.version)"
dotnet --version
dotnet --list-sdks

# 检查数据库文件
Test-Path database/dev/*.db
Test-Path database/prod/*.db

# 检查 DuckDB 原生库
Test-Path src/BomAddIn/duckdb.dll

# 验证解决方案完整性
dotnet restore BomAddIn.sln --force-evaluate
```

### 状态重置

```powershell
# 清理构建产物
dotnet clean BomAddIn.sln

# 重置 NuGet 缓存（仅在依赖损坏时）
dotnet nuget locals all --clear

# 重新还原和构建
dotnet restore BomAddIn.sln; dotnet build BomAddIn.sln
```

## 历史经验（从 diff 提炼）

### 高频修复模式

| 模式 | 出现次数 | 根因 |
|------|----------|------|
| BOM CTE 路径爆炸 | 2 | 递归 CTE 无去重，指数级膨胀 |
| DuckDB 类型推断 | 2 | 列类型推断与预期不符 |
| Excel-DNA 加载兼容 | 3 | ExternalLibrary vs Reference 混淆 |
| CI workflow 语法 | 2 | step-level strategy.matrix 无效 |
| DbUp 迁移失败 | 2 | 替换为 System.Data.SQLite runner |
| 深度审查多轮修复 | 5+ 轮 | 90 项发现分批处理 |

### 关键设计决策

- 离线优先架构：SQLite 本地缓存 + 恢复后自动同步
- 双引擎数据层：SQLite (CRUD) + DuckDB (分析)
- BFS 迭代替代递归 CTE：防止路径爆炸
- 适配器模式 ERP 同步：内置指数退避重试 + Excel 导入备用通道

## 开发流程

### 修改前（强制）

1. **Read** 对应 Skill 文件（Skill-first）
2. 检查调用者与影响范围
3. 确认不违反红线规则

### 遇到 Bug 时

1. 写最小复现测试 → confirm: FAILS
2. 修复 → confirm: PASSES + 无回归
3. **保留复现测试**
4. 检查是否需要更新 spec / skill

### 提交前必检

- [ ] 所有新代码有对应的测试
- [ ] 无跨线程 COM 调用
- [ ] 命名空间与文件夹一致
- [ ] 没动无关文件
- [ ] `dotnet build` + `dotnet test` 全绿

## 防幻觉铁律

| 铁律 | 说明 |
|------|------|
| **不靠记忆引用文档** | 先 Read/Grep 确认 |
| **不确定 = 承认** | 去查 spec |
| **写过的 = 读过的** | Read 它再改 |
| **版本号是事实锚点** | 每个结论标注来源文档版本，防止误用过时信息 |

## 会话管理

### 何时自查

- **每完成一个独立功能点** — 对照四条核心准则自检
- **上下文超过 5 个文件 / 20 轮对话** — 提醒用户开新会话

### 跨会话接力

```
上一个会话结束时 → 简述：
  ✅ 已完成 / 🔜 下一步 / ⚠️ 待决策 / 📄 关键上下文
```

### 基本原则

- 新会话先读本文件 + 对应 Skill
- 跨会话通过 git commit 衔接
- 每个 commit 自包含、可追溯

## 参考

| 文档 | 角色 |
| :--- | :--- |
| [readme.md](readme.md) | 用户入口、模块速览、使用模式 |
| [api-reference.md](rules/api-reference.md) | 签名唯一信源 |
| [user-manual.md](rules/user-manual.md) | 用户手册 |
| [context.md](rules/context.md) | 术语表 |
| [project-structure.md](rules/project-structure.md) | 结构地图 |
| [documentation.md](rules/documentation.md) | 文档职责 |
| [code-review-prompt.md](rules/code-review-prompt.md) | 审查模板 |
| [refactoring-plan.md](rules/refactoring-plan.md) | 重构计划 |
