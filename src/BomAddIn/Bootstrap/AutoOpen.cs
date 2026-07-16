using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BomAddIn.Bridge;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;
using BomAddIn.Dashboard;
using BomAddIn.Ribbon;
using BomAddIn.UI.TaskPane;
using BomAddIn.UDF;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Infrastructure.Models.Enums;
using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn
{
    /// <summary>
    /// Excel-DNA 插件入口。
    /// 启动链路（skill excel-dna-di-startup §1-§2）：
    ///   (1) 捕获 Excel 主线程 ID
    ///   (2) 初始化 DI 容器
    ///   (3) 启动探针自检（快速失败）
    ///   (4) 数据库迁移（同步执行）
    ///   (5) 种子数据 + 预热
    ///   (6) 日志就绪
    /// </summary>
    public class BomAddInStartup : IExcelAddIn
    {
        private static IServiceProvider? _serviceProvider;

        /// <summary>
        /// 全局 DI 容器（供 UDF 等无 DI 注入的上下文使用）。
        /// </summary>
        public static IServiceProvider ServiceProvider =>
            _serviceProvider ?? throw new InvalidOperationException(
                "DI 容器尚未初始化。AutoOpen 必须在此调用之前执行。");

        public void AutoOpen()
        {
            try
            {
                // 逐步恢复: 日志 + 线程 + DI
                try { LogConfigurator.Initialize(); } catch { }
                try { ExcelThreadDispatcher.Initialize(); } catch { }
                try
                {
                    _serviceProvider = ServiceConfigurator.Configure();
                    Container.Initialize(_serviceProvider);
                }
                catch (Exception ex) { ShowStartupError("DI失败", ex); return; }
                try { _serviceProvider.GetRequiredService<DatabaseMigrator>().RunPendingMigrations(); }
                catch (Exception ex) { ShowStartupError("迁移失败", ex); return; }
                try { SeedDefaultData(); }
                catch (Exception ex)
                {
                    AppLogger.Error($"管理员种子数据创建失败。请检查 BOM_ADMIN_SEED_PASSWORD 环境变量是否已设置。详情: {ex.Message}",
                        ex, typeof(BomAddInStartup));
                }
                // 后台预热和自动维护任务（fire-and-forget，失败不阻止启动）
                // H-31: 改为顺序执行（避免 3 并发 SQLite 写冲突），并记录失败原因
                var ct = CancellationToken.None;
                Task.Run(() =>
                {
                    try { WarmUpDuckDb(ct); }
                    catch (Exception ex) { AppLogger.Warn($"DuckDB 预热失败: {ex.Message}", typeof(BomAddInStartup)); }
                    try { CreateDailySnapshotIfNeeded(ct); }
                    catch (Exception ex) { AppLogger.Warn($"每日快照失败: {ex.Message}", typeof(BomAddInStartup)); }
                    try { GenerateSeedDataIfNeeded(ct); }
                    catch (Exception ex) { AppLogger.Warn($"种子数据检查失败: {ex.Message}", typeof(BomAddInStartup)); }
                });
                RegisterTaskPane();
                RegisterExcelCloseEvent();
            }
            catch (Exception ex) { ShowStartupError("启动异常", ex); }
        }

        private static void ShowStartupError(string title, Exception ex)
        {
            try
            {
                var message = $"{title}:\n{ex.Message}\n\n" +
                              "请运行 BomAddIn.Diagnostic.exe 检查环境配置。\n" +
                              $"详情: {ex}";
                MessageBox.Show(message, "BOM Suite — 启动错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // MessageBox 也可能失败（无 UI 线程等）
                System.Diagnostics.Debug.WriteLine($"[BomAddIn] {title}: {ex}");
            }
        }

        public void AutoClose()
        {
            WpfHelper.Shutdown();
            (_serviceProvider as IDisposable)?.Dispose();
            _serviceProvider = null;
        }

        private static void SeedDefaultData()
        {
            using var scope = ServiceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<BomAddIn.Core.Services.IAuthService>();
            authService.SeedAdminUser();
        }

        /// <summary>
        /// DuckDB 预热 — 在 Task.Run 内创建独立 scope 和连接，
        /// 避免外部 scope 提前释放导致连接失效 (R2-09)。
        /// </summary>
        private static void WarmUpDuckDb(CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return;
                using var scope = ServiceProvider.CreateScope();
                var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
                var analysisProvider = scope.ServiceProvider.GetRequiredService<IBomAnalysisProvider>();

                // 在 scope 内完成所有 DuckDB 加载，不依赖外部连接生命周期
                using var sqliteConn = connectionFactory.CreateConnection();
                sqliteConn.Open();
                analysisProvider.LoadFromSqlite(sqliteConn);
            }
            catch (Exception ex)
            {
                // 预热失败不阻止启动，首次 BOM 展开时会触发懒加载
                AppLogger.Warn($"DuckDB 预热失败: {ex.Message}", typeof(BomAddInStartup));
            }
        }

        private static void CreateDailySnapshotIfNeeded(CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return;
                using var scope = ServiceProvider.CreateScope();
                var snapshotService = scope.ServiceProvider.GetRequiredService<BomAddIn.Core.Services.ISnapshotService>();
                snapshotService.CreateSnapshot(UserRole.Admin, "Daily", $"Auto-snapshot at startup: {DateTime.Today:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"每日快照创建失败（不阻止启动）: {ex.Message}", typeof(BomAddInStartup));
            }
        }

        private static void GenerateSeedDataIfNeeded(CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return;
                using var scope = ServiceProvider.CreateScope();
                var generator = scope.ServiceProvider.GetRequiredService<BomAddIn.Core.Services.ISeedDataGenerator>();

                if (!generator.HasSeedData())
                {
                    // R2-13: 开发环境默认 5000 物料 + 25000 BOM 节点 + 6 个月历史
                    var result = generator.Generate(UserRole.Admin, materialCount: 5000, bomNodeCount: 25000, historyMonths: 6);
                    if (!result.Skipped)
                    {
                        AppLogger.Info(
                            $"Seed data generated: {result.MaterialsCreated} materials, " +
                            $"{result.BomNodesCreated} BOM nodes, " +
                            $"{result.PriceRecordsCreated} price records, " +
                            $"{result.InventoryRecordsCreated} inventory records",
                            typeof(BomAddInStartup));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"种子数据生成失败（不阻止启动）: {ex.Message}", typeof(BomAddInStartup));
            }
        }

        private static void RegisterTaskPane()
        {
            try
            {
                // WPF Application 需在当前线程存在以解析主题资源（默认 Button/TextBox 样式等）。
                // 使用 WpfHelper 协调——WPF 单 AppDomain 只允许一个 Application，
                // DashboardBootstrapper 在独立 STA 线程创建前会检查 IsApplicationCreated。
                WpfHelper.EnsureInitialized();

                var app = (Microsoft.Office.Interop.Excel.Application)ExcelDnaUtil.Application;
                var taskPane = CustomTaskPaneFactory.CreateCustomTaskPane(
                    typeof(BomTaskPane), "BOM Suite", app);
                taskPane.Visible = true;
                taskPane.DockPosition = ExcelDna.Integration.CustomUI.MsoCTPDockPosition.msoCTPDockPositionRight;
                taskPane.Width = 340;

                // 关联 Ribbon 按钮
                RibbonController.ShowTaskPane = () =>
                {
                    taskPane.Visible = true;
                };
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 非 Excel 宿主（如诊断工具）——TaskPane 不可用，静默跳过
            }
            catch (Exception ex)
            {
                // 其他 WPF/Excel-DNA 异常（XamlParseException、InvalidOperationException 等）
                // 不阻止启动——TaskPane 是辅助功能
                Infrastructure.Logging.AppLogger.Warn(
                    $"TaskPane 注册失败: {ex.Message}", typeof(BomAddInStartup));
            }
        }

        private static void RegisterExcelCloseEvent()
        {
            try
            {
                var app = (Microsoft.Office.Interop.Excel.Application)ExcelDnaUtil.Application;
                app.WorkbookBeforeClose += (Microsoft.Office.Interop.Excel.Workbook wb, ref bool cancel) =>
                {
                    if (app.Workbooks.Count <= 1)
                    {
                        DashboardBootstrapper.Close();
                    }
                };
            }
            catch
            {
                // 非 Excel 宿主（如诊断工具中加载）时静默忽略
            }
        }
    }
}
