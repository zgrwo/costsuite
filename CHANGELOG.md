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

## [1.0.0] - 2025-07-01

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
