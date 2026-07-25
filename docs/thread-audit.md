# 线程审计报告

> 审计日期: 2026-07-26 | 审计范围: src/BomAddIn/ 全部 COM 调用点

## 结论

**零违规** — 所有 Excel COM 调用均在 Excel 主线程执行，后台线程仅访问 SQLite/DuckDB。

## COM 调用点清单

| 文件 | 行号 | 调用 | 线程 | 状态 |
|------|------|------|------|------|
| Bootstrap/AutoOpen.cs | L195 | `ExcelDnaUtil.Application` (TaskPane 创建) | Excel 主线程 (AutoOpen) | ✅ 安全 |
| Bootstrap/AutoOpen.cs | L225 | `ExcelDnaUtil.Application` (WorkbookBeforeClose) | Excel 主线程 (AutoOpen) | ✅ 安全 |
| Bridge/VersionAdapter.cs | — | `Application.Version` 检测 | Excel 主线程 (UDF) | ✅ 安全 |

## 后台线程活动（无 COM 调用）

| 文件 | 活动 | 访问资源 | 状态 |
|------|------|----------|------|
| Bootstrap/AutoOpen.cs L71 | `Task.Run(WarmUpDuckDb)` | SQLite + DuckDB | ✅ 安全 |
| UI/TaskPane/BomTaskPane.xaml.cs | `Task.Run` (同步/展开/快照) | SQLite + DuckDB via Services | ✅ 安全 |
| Ribbon/RibbonController.cs | `Task.Run` (文件解析/导入) | SQLite via Services | ✅ 安全 |
| Dashboard/DashboardViewModel.cs | `Task.Run` (数据加载) | SQLite + DuckDB via Services | ✅ 安全 |

## UDF 线程模型

所有 UDF 均标记 `IsThreadSafe = false`，Excel 在主线程调用，无跨线程风险。

## UI 线程封送

- WPF 属性更新通过 `_uiDispatcher.Invoke()` / `_uiDispatcher.CheckAccess()` 保护
- ExcelThreadDispatcher 可用但当前无后台→主线程 COM 调用需求

## 压力测试验证

`ThreadStressTests` (BOM_STRESS_MINUTES=30): 6 路并发工作者，30min 0 异常。
