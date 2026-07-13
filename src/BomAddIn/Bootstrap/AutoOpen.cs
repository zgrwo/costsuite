using System;
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
                // R2-07: NLog 最先初始化，确保后续所有步骤的日志可记录
                LogConfigurator.Initialize();

                // 1. 捕获 Excel 主线程 ID
                ExcelThreadDispatcher.Initialize();

                // 2. 初始化 DI 容器
                _serviceProvider = ServiceConfigurator.Configure();

                // 2a. 初始化 UDF Container（服务定位器）
                Container.Initialize(_serviceProvider);

                // 3. 启动探针自检（快速失败）
                StartupValidator.Validate(_serviceProvider);

                // 4. 数据库迁移（同步执行，必须完成才能用 DAL）
                var migrator = _serviceProvider.GetRequiredService<DatabaseMigrator>();
                migrator.RunPendingMigrations();

                // 5. 种子管理员账户（幂等，必须同步完成——后续登录依赖此账户）
                SeedDefaultData();

                // 5a. 创建每日快照（后台执行，不阻塞 Excel UI）
                Task.Run(() => CreateDailySnapshotIfNeeded());

                // 6. 预热 DuckDB（后台加载，不阻塞启动）
                // R2-09: 在 Task.Run 内部创建独立 scope，避免外部 scope 提前释放
                Task.Run(() => WarmUpDuckDb());

                // 6a. 种子数据（首次启动时后台生成，不阻塞 Excel UI）
                // R2-13: 提升至开发环境默认 5000 物料 + 25000 BOM 节点
                Task.Run(() => GenerateSeedDataIfNeeded());

                // 7. 日志环境信息
                var envManager = _serviceProvider!.GetRequiredService<BomAddIn.Infrastructure.Config.EnvironmentManager>();
                var dbFactory = _serviceProvider.GetRequiredService<IDbConnectionFactory>();
                AppLogger.Info($"当前环境: {envManager.Current} | 数据库: {((SqliteConnectionFactory)dbFactory).DatabaseFilePath}", typeof(BomAddInStartup));

                // 8. 注册 TaskPane 和 Ribbon 关联
                RegisterTaskPane();

                // 9. 注册 Excel 关闭事件
                RegisterExcelCloseEvent();
            }
            catch (Exception ex)
            {
                var message = $"BOM Add-In 启动失败:\n{ex.Message}\n\n" +
                              "请运行 BomAddIn.Diagnostic.exe 检查环境配置。";
                MessageBox.Show(message, "BOM Suite — 启动错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        public void AutoClose()
        {
            // 关闭 WPF Dashboard
            DashboardBootstrapper.Close();

            // 清理资源
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
        private static void WarmUpDuckDb()
        {
            try
            {
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

        private static void CreateDailySnapshotIfNeeded()
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var snapshotService = scope.ServiceProvider.GetRequiredService<BomAddIn.Core.Services.ISnapshotService>();
                snapshotService.CreateSnapshot("Daily", $"Auto-snapshot at startup: {DateTime.Today:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"每日快照创建失败（不阻止启动）: {ex.Message}", typeof(BomAddInStartup));
            }
        }

        private static void GenerateSeedDataIfNeeded()
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var generator = scope.ServiceProvider.GetRequiredService<BomAddIn.Core.Services.ISeedDataGenerator>();

                if (!generator.HasSeedData())
                {
                    // R2-13: 开发环境默认 5000 物料 + 25000 BOM 节点 + 6 个月历史
                    var result = generator.Generate(materialCount: 5000, bomNodeCount: 25000, historyMonths: 6);
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
            catch
            {
                // TaskPane 注册失败不阻止启动（非 Excel 宿主环境下）
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
