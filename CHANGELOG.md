# Changelog

本文件记录成本分析套件（BomAddIn）的所有重要变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.0] - 2026-07-26

### Added

- BOM Closure Table 预计算（S006 迁移脚本 + 触发器自动维护）
- IBomClosureRepository / BomClosureRepository（O(1) 子树/祖先查询）
- BomAnalysisProvider.ExpandBomViaClosure（Closure Table 展开，无数据时 fallback BFS）
- BomClosureTableTests（菱形/深链/循环/一致性 边界测试）
- ThreadStressTests（30s/30min 可配置压力测试，BOM_STRESS_MINUTES 环境变量）
- BomExpansionBenchmarks（Closure vs BFS 性能对比基准）
- 新增 CONTRIBUTING.md 贡献指南
- 新增 CHANGELOG.md 变更日志

### Changed

- BomService.Expand 切换为 Closure Table 优先路径（ExpandBomViaClosure）
- 移除 Polly 依赖，SyncService 改用内置指数退避重试（无第三方库）
- 移除 NLog 依赖，改用轻量 FileLogSink（AppLogger 门面 + 文件/Debug 双通道）
- 移除 AES-256 加密 DI 注册（代码保留，推迟至 V2.0 启用）
- 更新 Excel 基准版本为 2019+（原 2016/2019）
- specification.md 同步更新：移除 gRPC/Polly/NLog 相关描述

### Fixed

- **P0**: 修复 10 处 SQLite 连接 double-open（BomService/SyncService/SnapshotService/
  ApprovalService/BomExcelImporter/AutoOpen）— CreateConnection() 已返回打开的连接，
  重复 Open 导致 BOM 编辑、同步写入、快照、导入路径全部崩溃
- **P0**: 修复 BomNodeRepository.Update 匿名参数缺少 Id 导致的参数绑定失败
- **P0**: 修复 BomVersions.BomId 外键指向错误（S007 迁移：Materials → BomStructures，
  与 specification.md §4.4.3 及业务代码语义对齐）
- **P1**: 修复 CI 发行 ZIP 打包扁平化目录导致 SQLite.Interop.dll (x86/x64) 互相覆盖的问题
- **P1**: 修复 api-reference/context 文档中 reference/ 前缀断链，BOMEXPAND/ALERTCHECK
  行为描述与实际实现对齐（截断哨兵行、完整预警规则表）
- **P2**: Closure Table 重建添加耗时监控（超 30s 告警，高共享件数据防路径爆炸风险可见化）
- **P3**: ALERTCHECK 价格比较改用记录实际币种（原硬编码 CNY 掩盖币种差异检测）
- **P3**: BOMCOST BFS 路径成本汇总改用 decimal（与 Closure 路径精度策略对齐）
- 二次审查修复：S007 补 Estimates 悬空引用保护（BomVersionId 重建为可空 +
  版本删除时置 NULL 触发器）；清理 spec §9.3/project-structure/skills 中事件总线
  悬挂引用；CI 发行 ZIP 改用 staging 布局，解压根目录即分发结构

### Removed

- **P2 (YAGNI)**: 移除零生产者/零消费者的事件总线（IEventBus/ExcelEventBus + 4 个事件类）
  及其 DI 注册；specification.md §2.4 同步标记为 V1.1 移除、V2.0 按需重引

## [1.0.0] - 2026-07-23

### Added

- BOM 展开引擎（BFS 迭代，maxLevel=20，HashSet 去重）
- 差异分析计算器（复合键分组，确定性排序）
- ERP 同步服务（并行拉取 5 表，事务批量写入）
- Excel-DNA UDF 函数库（BOMEXPAND, BOMCOST, VARIANCECHECK 等）
- WPF 仪表盘（KPI 概览、趋势图、预警面板）
- RBAC 权限控制（Admin/Analyst/Viewer 三角色）
- SQLite + DuckDB 双引擎数据层
- xUnit 单元/集成测试套件
- CI/CD pipeline（GitHub Actions）
