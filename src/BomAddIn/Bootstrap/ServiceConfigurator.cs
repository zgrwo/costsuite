using BomAddIn.Bridge;
using BomAddIn.Core.Events;
using BomAddIn.Core.Services;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;
using BomAddIn.Data.Repositories;
using BomAddIn.Data.Sync;
using BomAddIn.EventBus;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Network;
using BomAddIn.Infrastructure.Security;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn
{
    /// <summary>
    /// DI 容器配置。按层分组的注册方法。
    /// 参见 spec §2.3、skill excel-dna-di-startup §2。
    /// </summary>
    public static class ServiceConfigurator
    {
        public static ServiceProvider Configure()
        {
            var services = new ServiceCollection();

            RegisterInfrastructure(services);
            RegisterBridge(services);
            RegisterData(services);
            RegisterCore(services);
            // C-3: 注册事件总线 (Singleton，进程内 pub-sub)
            RegisterEventBus(services);

            var provider = services.BuildServiceProvider();

            // 初始化 EnvironmentManager — 从 AppConfig 表读取当前环境
            InitializeEnvironment(provider);

            // 初始化 AppConfigProvider — 从数据库加载配置覆盖默认值
            InitializeConfigProvider(provider);

            return provider;
        }

        private static void RegisterEventBus(IServiceCollection services)
        {
            services.AddSingleton<IEventBus, ExcelEventBus>();
        }

        private static void InitializeEnvironment(ServiceProvider provider)
        {
            try
            {
                var envManager = provider.GetRequiredService<EnvironmentManager>();
                // 使用 PROD 连接字符串先读取环境配置（因为此时还不知道环境）
                // 先用默认 PROD 连接检查配置表中的环境设置
                using var conn = new System.Data.SQLite.SQLiteConnection(
                    $"Data Source={SqliteConnectionFactory.ProdDatabasePath}");
                conn.Open();

                // 尝试查 AppConfig 表获取当前环境
                try
                {
                    var env = conn.QueryFirstOrDefault<string>(
                        "SELECT Value FROM AppConfig WHERE Key = 'Environment:Current'");
                    if (!string.IsNullOrWhiteSpace(env))
                        envManager.LoadFromDb($"Data Source={SqliteConnectionFactory.ProdDatabasePath}");
                }
                catch
                {
                    // 表不存在时使用默认 PROD
                }
            }
            catch
            {
                // 初始化失败不阻止启动
            }
        }

        private static void InitializeConfigProvider(ServiceProvider provider)
        {
            try
            {
                var configProvider = (AppConfigProvider)provider.GetRequiredService<IConfigProvider>();
                var dbFactory = provider.GetRequiredService<IDbConnectionFactory>();
                configProvider.LoadFromDb(dbFactory.ConnectionString);
            }
            catch
            {
                // 数据库不可用时使用硬编码默认值，不阻止启动
            }
        }

        private static void RegisterInfrastructure(IServiceCollection services)
        {
            // Security
            services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
            // DPAPI: DEK 保护（被 AesEncryptionProvider 内部使用）
            services.AddSingleton<DpapiEncryptionProvider>();
            // AES-256-CBC: 数据加密（spec §11.3）
            services.AddSingleton<IEncryptionProvider, AesEncryptionProvider>();

            // Config (Singleton — 启动时从 AppConfig 表加载覆盖值)
            services.AddSingleton<IConfigProvider, AppConfigProvider>();

            // Environment (Singleton — DEV/PROD 切换)
            services.AddSingleton<EnvironmentManager>();

            // Network
            services.AddSingleton<INetworkMonitor, NetworkMonitor>();
        }

        private static void RegisterBridge(IServiceCollection services)
        {
            services.AddSingleton<IExcelThreadDispatcher, ExcelThreadDispatcher>();
            services.AddSingleton<IVersionAdapter, VersionAdapter>();
        }

        private static void RegisterData(IServiceCollection services)
        {
            // Connection (Singleton — DEV/PROD 环境隔离)
            services.AddSingleton<IDbConnectionFactory>(sp =>
                new SqliteConnectionFactory(sp.GetRequiredService<EnvironmentManager>()));

            // Migration (Singleton — run once at startup)
            services.AddSingleton<DatabaseMigrator>();

            // Caching (Singleton — process-wide L1 cache)
            services.AddSingleton<ICacheProvider, MemoryCacheProvider>();

            // Repositories (Scoped per operation)
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IMaterialRepository, MaterialRepository>();
            services.AddScoped<ISupplierRepository, SupplierRepository>();
            services.AddScoped<IAppConfigRepository, AppConfigRepository>();
            services.AddScoped<IUserTokenRepository, UserTokenRepository>();
            services.AddScoped<IBomNodeRepository, BomNodeRepository>();
            services.AddScoped<IPriceRecordRepository, PriceRecordRepository>();
            services.AddScoped<IInventoryRecordRepository, InventoryRecordRepository>();
            services.AddScoped<IOrderRecordRepository, OrderRecordRepository>();
            services.AddScoped<ICapacityRecordRepository, CapacityRecordRepository>();
            services.AddScoped<IBomVersionRepository, BomVersionRepository>();

            // Analysis (Singleton — DuckDB 内存引擎，进程级唯一)
            services.AddSingleton<IBomAnalysisProvider, BomAnalysisProvider>();

            // Audit (Scoped — 审计日志写入)
            services.AddScoped<IAuditLogRepository, AuditLogRepository>();

            // SyncLog (Scoped — 同步日志)
            services.AddScoped<ISyncLogRepository, SyncLogRepository>();

            // Snapshot (Scoped — 数据快照)
            services.AddScoped<IDataSnapshotRepository, DataSnapshotRepository>();

            // Sync (Singleton — ERP 适配器)
            services.AddSingleton<IErpAdapter, SimulatedErpAdapter>();
        }

        private static void RegisterCore(IServiceCollection services)
        {
            // Services (Scoped per operation)
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IBomService, BomService>();
            services.AddScoped<ISyncService, SyncService>();
            services.AddScoped<IVarianceService, VarianceService>();

            // Authorization (Singleton — stateless, hot path)
            services.AddSingleton<IAuthorizationService, AuthorizationService>();

            // Config (Singleton — backed by cache)
            services.AddSingleton<IConfigService, ConfigService>();

            // Variance Engine (Singleton — stateless pure calculation)
            services.AddSingleton<IVarianceCalculator, VarianceCalculator>();
            services.AddSingleton<IAlertEvaluator, AlertEvaluator>();

            // Excel Import (Singleton — stateless)
            services.AddSingleton<IBomExcelImporter, BomExcelImporter>();

            // Audit (Scoped — 业务审计日志)
            services.AddScoped<IAuditService, AuditService>();

            // Approval (Scoped — BOM 审批工作流)
            services.AddScoped<IApprovalService, ApprovalService>();

            // Snapshot (Scoped — 数据快照)
            services.AddScoped<ISnapshotService, SnapshotService>();

            // Seed Data (Singleton — 种子数据生成器)
            services.AddSingleton<ISeedDataGenerator, SeedDataGenerator>();
        }
    }
}
