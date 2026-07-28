# costsuite (BomAddIn) — 重构计划

> 基于 19 commits 全量历史分析 | 目标：从"原型验证"到"生产就绪"
> 项目成熟度：★★☆☆☆（早期项目，先完善再优化）
> 对标项目：BOMManagementSoftware / Excel-DNA 企业级插件最佳实践
> ⚠️ 核心审查结论：**架构过度设计**（19 commits 原型不需要 6 层 + DI + MediatR + Polly）

## 1. 现状评估

### 1.1 优势（必须保留）

| 维度 | 现状 | 评价 |
|------|------|------|
| 规格文档 | 919 行技术规格说明书 | ★★★★★ 最完整 |
| Skill 体系 | 6 个专项 Skill 文件 | ★★★★★ |
| 离线优先 | SQLite+DuckDB 本地主库 | ★★★★☆ |
| 线程隔离 | ExcelThreadDispatcher 强制 | ★★★★☆ |
| 架构意识 | 六层严格单向依赖 | ★★★★☆ 设计好但实施过早 |

### 1.2 痛点（历史反复出错）

| 痛点 | 出现次数 | 根因 | 优先级 |
|------|----------|------|--------|
| BOM CTE 路径爆炸 | 1 次（关键修复） | 递归 CTE 无深度限制/去重 | P0 |
| 线程隔离违反 | 3+ 次 | WPF/Task.Run 直接调 COM | P0 |
| Excel-DNA 加载兼容 | 2+ 次 | ExplicitExports/Reference 配置 | P1 |
| DuckDB 类型推断 | 1 次 | 动态类型与 C# 静态类型冲突 | P1 |
| 5 层深度审查 90 项 | 1 次 | 初始实现质量不足 | P1 |

### 1.3 架构过度设计评估（关键审查结论）

| 当前设计 | 问题 | 建议 |
|---------|------|------|
| 6 层架构 | 19 commits 原型不需要 6 层 | **合并为 4 层**：UI→Service→Engine→Data |
| MediatR 事件总线 | 无实际事件消费者 | **删除**，v2.0 有需求再加 |
| Polly 重试 | 离线系统无网络调用 | **删除**，ERP 同步是 v2.0 |
| NLog + AOP 审计 | 原型阶段无审计需求 | **简化为 Console/Debug 日志** |
| AES-256 加密 | 本地 SQLite 无安全威胁 | **删除**，v2.0 有敏感数据再加 |
| ERP 同步/gRPC/审批流 | 未验证假设 | **从规格文档移除**，聚焦核心 |
| Excel 2016 基准 | 2026 年 2016 已 EOL | **改为 Excel 2019+** |

**简化后的 4 层架构**：
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

> **为什么是 4 层而非 3 层？** VarianceCalculator 是纯计算逻辑（无 IO、无状态），
> 与 BomService（业务编排、有事务）性质不同。独立为 Engine 层后：
> - 差异算法可独立单元测试（无需 mock Service 依赖）
> - 未来算法升级（如结构比对替代逐字段比对）不波及 Service
> - 仍比原 6 层少 2 层，保持原型简洁度

### 1.4 技术债（需审计确认）

> ⚠️ 项目仅 19 commits，以下技术债为推测，需 Phase 0 审计确认实际范围

- [ ] 测试覆盖率不足（仅 UDF 参数解析 + 2 Repository + SyncService）
- [ ] 缺少集成测试（BOM 展开/差异计算/同步）
- [ ] 缺少性能测试（10 万物料/100 万节点）
- [ ] CI workflow 损坏（step-level strategy.matrix 无效）
- [x] ~~迁移脚本从 DbUp 改为 System.Data.SQLite~~（已完成，DatabaseMigrator.cs）
- [x] ~~无 LICENSE / CONTRIBUTING.md / CHANGELOG~~（已补齐）
- [ ] 规格文档包含未验证功能（ERP/gRPC/审批流）

## 2. 重构目标

### 2.1 核心目标

1. **架构简化**（P0）：6 层→4 层（UI→Service→Engine→Data），删除未使用的企业级组件
2. **CI 修复**（P0）：workflow 可运行，PR 自动验证
3. **关键路径测试**（P0）：BOM 展开/差异计算有测试覆盖
4. **BOM 递归修复**（P0）：Closure Table 替代递归 CTE
5. **性能 baseline**（P1）：测量当前性能，再设改进目标

### 2.2 非目标

- ❌ 不增加新功能（v1.2 再议）
- ❌ 不迁移数据库（SQLite+DuckDB 已验证）
- ❌ **不支持 Excel 2016**（已 EOL，基准改为 2019+）
- ❌ **不实现 ERP 同步/gRPC/审批流**（未验证假设，v2.0 再议）
- ❌ **不进行大规模性能优化**（先有 baseline 再说）
- ❌ **不保留 MediatR/Polly/AES-256/NLog**（原型不需要）

### 2.3 CI 损坏原因说明

```yaml
# ❌ 错误：step-level strategy.matrix 无效
- name: Test
  strategy:
    matrix:
      os: [ubuntu-latest, windows-latest]

# ✅ 正确：job-level strategy.matrix
jobs:
  test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
```

### 2.4 BOM 递归方案对比

| 方案 | 优点 | 缺点 | 适用场景 |
|------|------|------|---------|
| 递归 CTE | SQL 简洁 | 路径爆炸/循环引用/深度限制 | ❌ 不适合 BOM |
| BFS 迭代 + HashSet | 安全/可控 | 代码量稍多 | ✅ 当前方案 |
| **Closure Table** | O(1) 查询任意层级 | 需维护额外表 | ✅✅ 推荐（v1.1） |

**Closure Table 设计**：
```sql
-- 预计算所有祖先-后代关系
CREATE TABLE bom_closure (
    ancestor_id   TEXT NOT NULL,
    descendant_id TEXT NOT NULL,
    depth         INTEGER NOT NULL,
    PRIMARY KEY (ancestor_id, descendant_id)
);

-- 查询某节点的所有子件：O(1)
SELECT descendant_id, depth FROM bom_closure
WHERE ancestor_id = @nodeId AND depth > 0;

-- 查询某节点的所有父件：O(1)
SELECT ancestor_id, depth FROM bom_closure
WHERE descendant_id = @nodeId AND depth > 0;
```

## 3. 重构方案

### 3.0 Phase 0: 技术债审计 + 架构决策（3-5 天）【P0，必须先做】

**目标**：确认实际技术债范围 + 确认架构简化方案

| 任务 | 产出 | 验收标准 |
|------|------|----------|
| 测试覆盖率审计 | `dotnet test --collect:"XPlat Code Coverage"` | 记录当前覆盖率百分比 |
| 性能 baseline 测量 | 手动测试 BOM 展开（1000/10000 物料） | 记录耗时（ms） |
| CI 损坏原因确认 | 检查 workflow 日志 | 明确错误原因 |
| 线程调用点审计 | `grep -rn "Application\." src/` | 记录 COM 调用点数量 |
| 未使用组件审计 | 检查 MediatR/Polly/NLog/AES 实际调用 | 确认哪些可安全删除 |
| 规格文档清理 | 标记未验证功能 | 列出需移除的章节 |

**决策点**：
- 如果 MediatR/Polly/AES 零调用 → Phase 1 直接删除
- 如果测试覆盖率 >60% → Phase 1 仅补关键路径
- 如果测试覆盖率 <30% → Phase 1 需要大规模补测试
- 如果 BOM 展开 10000 物料 <2s → Phase 2 性能优化可推迟

### 3.1 Phase 1: 工程化基础 + CI 修复 + 架构简化（1-2 周）【P0】

**目标**：补齐开源基本要素 + CI 可运行 + 删除过度设计

| 任务 | 产出 | 验收标准 | 依赖 |
|------|------|----------|------|
| 添加 LICENSE | `LICENSE`（MIT） | 文件存在 | — |
| 添加 CONTRIBUTING.md | `CONTRIBUTING.md` | 含架构说明/开发流程 | — |
| 添加 CHANGELOG.md | `CHANGELOG.md`（keepachangelog） | 含历史版本 | — |
| 修复 CI workflow | `.github/workflows/ci.yml` | PR 自动运行，全绿 | Phase 0 |
| Issue/PR 模板 | `.github/ISSUE_TEMPLATE/` | bug/feature 模板 | — |
| 删除未使用组件 | 移除 MediatR/Polly/AES/NLog | 编译通过 + 测试通过 | Phase 0 确认 |
| 规格文档清理 | 移除 ERP/gRPC/审批流章节 | 仅保留已验证功能 | Phase 0 |
| Excel 基准更新 | 规格文档修改 | 2019+ 为基准 | — |

**回滚策略**：基础设施是新增文件；组件删除在分支进行，编译+测试验证。

### 3.2 Phase 2: 关键路径测试 + BOM 修复（1-2 周）【P0】

**目标**：BOM 展开/差异计算有测试 + Closure Table 替代递归 CTE

| 任务 | 产出 | 验收标准 | 依赖 |
|------|------|----------|------|
| BOM 展开集成测试 | `tests/BomExpandTests.cs` | 多级 BOM/循环引用/深度限制 | Phase 1 |
| 差异计算集成测试 | `tests/VarianceTests.cs` | 新增/删除/修改/数量差异 | — |
| Closure Table 实现 | `Data/BomClosureRepository.cs` | 替代递归 CTE | Phase 1 |
| Closure Table 迁移脚本 | `migrations/add_closure_table.sql` | 自动填充现有数据 | 上一项 |
| 同步服务测试 | `tests/SyncServiceTests.cs` | 离线/在线/冲突解决 | — |

**BOM 展开测试用例**：
```csharp
[Theory]
[InlineData("single_level", 3)]      // 单层 BOM
[InlineData("multi_level", 5)]       // 5 层嵌套
[InlineData("circular_ref", 0)]      // 循环引用 → 报错
[InlineData("diamond", 4)]           // 菱形依赖 → 去重
[InlineData("deep_50", 50)]          // 50 层深度 → 正常
[InlineData("deep_51", -1)]          // 51 层 → 超限报错
public void BomExpand_HandlesAllCases(string scenario, int expected) { }
```

**回滚策略**：测试是新增文件；Closure Table 在分支开发，保留旧 CTE 代码直到验证通过。

### 3.3 Phase 3: 线程安全加固（1-2 周）【P0/P1，视审计结果】

**目标**：零 RPC_E_WRONG_THREAD 异常

| 任务 | 产出 | 验收标准 | 依赖 |
|------|------|----------|------|
| 线程审计 | `docs/thread-audit.md` | 所有 COM 调用点标记 | Phase 0 |
| ExcelThreadDispatcher 全覆盖 | 源码修复 | WPF/Timer/Task.Run 全走 Dispatcher | 上一项 |
| 压力测试 | `tests/ThreadingStressTests.cs` | 30min 连续运行 0 异常 | — |
| 反模式文档 | `docs/threading-antipatterns.md` | 常见错误 + 正确写法 | — |

**回滚策略**：线程修复逐文件提交，压力测试失败则 revert。

### 3.4 Phase 4: 性能优化 + 发布准备（按需）【P2】

| 任务 | 产出 | 验收标准 | 依赖 |
|------|------|----------|------|
| BOM 展开优化（如需要） | 基于 Closure Table 的 O(1) 查询 | 比 baseline 提升 50%+ | Phase 2 |
| 连接池优化（如需要） | `Data/ConnectionFactory.cs` | 连接复用 | — |
| 性能基准测试 | `benchmarks/BomBenchmark.cs` | 自动化基准 | Phase 2 |
| 迁移脚本验证 | `tests/MigrationTests.cs` | DEV→PROD 全路径 | Phase 0 |
| 离线安装脚本 | `scripts/offline-install.ps1` | 无网络环境可安装 | — |
| Semantic Versioning | git tag `v1.1.0` | 版本号与 CHANGELOG 一致 | — |

**注意**：性能优化必须在有 baseline 数据后才进行，避免盲目优化。

## 4. 里程碑与时间线

```
Phase 0 (3-5天): 技术债审计 + 架构决策 【必须先做】
  ├─ Day 1-2: 测试覆盖率 + 性能 baseline
  ├─ Day 3: CI 损坏原因 + 线程调用点 + 未使用组件
  └─ Day 4-5: 规格文档清理 + 架构简化决策

Phase 1 (1-2周): 工程化 + CI + 架构简化 【P0】
  ├─ LICENSE + CONTRIBUTING + CHANGELOG + CI
  ├─ 删除 MediatR/Polly/AES/NLog
  └─ 规格文档清理（移除未验证功能）

Phase 2 (1-2周): 关键测试 + BOM 修复 【P0】
  ├─ BOM/差异/同步测试
  └─ Closure Table 替代递归 CTE

Phase 3 (1-2周): 线程安全 【P0/P1，视审计结果】
  ├─ 线程审计 + Dispatcher 覆盖
  └─ 30min 压力测试

Phase 4 (按需): 性能 + 发布 【P2】
  ├─ Closure Table 性能验证
  └─ 安装脚本 + Semantic Versioning
```

## 5. 重构守卫（每 Phase 必须执行）

```
Phase 开始前：
  ① dotnet build（编译通过）
  ② dotnet test（现有测试全通过）
  → 记录通过数/失败数

Phase 结束后：
  ①② 同上
  → 对比：任何新增失败 = 立即回滚该 Phase 的修改
```

## 6. 风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| 架构简化删除了未来需要的组件 | 低 | 中 | 仅删除零调用组件；v2.0 可重新引入 |
| Closure Table 迁移数据不一致 | 中 | 高 | 迁移后全量校验（旧 CTE vs 新 Closure 结果对比） |
| BOM 展开性能不达标 | 中 | 高 | Closure Table O(1) + 缓存 |
| 线程问题难以复现 | 高 | 高 | 30min 压力测试 + 详细日志 |
| DuckDB 类型推断问题 | 中 | 中 | 显式类型转换 + 测试覆盖 |
| 规格文档清理遗漏 | 低 | 低 | 逐章节审查 + 标记"v2.0 再议" |

## 7. 验收标准

重构完成后，以下指标必须达成：

- [ ] CI workflow 全绿（PR 自动验证）
- [ ] 架构从 6 层简化为 4 层（零 MediatR/Polly/AES/NLog 引用）
- [ ] 关键路径测试覆盖率 >60%（Phase 0 baseline → 目标）
- [ ] BOM 展开使用 Closure Table（零递归 CTE）
- [ ] 线程压力测试 30min 0 异常
- [ ] 规格文档仅包含已验证功能（零 ERP/gRPC/审批流）
- [ ] LICENSE + CONTRIBUTING + CHANGELOG 完整
- [ ] Excel 基准为 2019+（规格文档已更新）

## 8. 历史经验教训（必须铭记）

### 8.1 BOM CTE 路径爆炸的教训

**根因**：递归 CTE 无深度限制，循环引用导致无限递归

**对策**：
- Closure Table 预计算所有祖先-后代关系
- BFS 迭代作为 fallback（Closure Table 未就绪时）
- 全局去重（HashSet）+ 深度限制（默认 50 级）

### 8.2 过度设计的教训（本次审查新增）

**根因**：19 commits 原型使用了企业级 6 层架构 + MediatR + Polly + AES-256

**对策**：
- 原型阶段用 4 层（UI→Service→Engine→Data）
- 企业级组件在有实际需求时再引入（YAGNI）
- 规格文档不写未验证功能

### 8.3 线程隔离违反的教训

**根因**：WPF/Task.Run 中直接调用 Excel COM，未走 Dispatcher

**对策**：
- 所有 COM 调用必须经 ExcelThreadDispatcher
- 线程审计文档标记所有调用点
- 压力测试验证

### 8.4 Excel-DNA 加载兼容的教训

**根因**：ExplicitExports/Reference 配置错误，依赖库重复注册

**对策**：
- 纯依赖库用 Reference，不用 ExternalLibrary
- ExplicitExports=true 避免重复注册
- 加载/卸载测试覆盖
