# Skill: Excel-DNA 依赖注入与启动引导

> **TRIGGER**: 修改 `src/BomAddIn/Bootstrap/`、`src/BomAddIn.Infrastructure/Config/`、`src/BomAddIn.Infrastructure/Security/` 下任何 `.cs` 文件时，或新增/修改 DI 注册时，**必须**先读此 Skill。
>
> **来源**: [Extensibility.ExcelDNA.Sample](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)、[Excel-DNA/Samples](https://github.com/Excel-DNA/Samples)  
> **适用范围**: `AutoOpen()` 启动流程、DI 容器配置、Excel-DNA 生命周期管理

---

## 1. 核心原则

```
启动链路（AutoOpen 中按顺序执行）：

  1. 捕获 Excel 主线程 ID
  2. 初始化 NLog（最先，确保后续步骤的日志可记录）
  3. 配置 DI 容器（分层注册）
  4. 运行启动健康检查（DB / 网络 / SQLite / 版本）
  5. 注册 Excel 事件（关闭、工作簿切换）
  6. 初始化 ExcelThreadDispatcher
  7. 加载 Ribbon / TaskPane（延迟初始化，不阻塞启动）

  任何步骤失败 → 记录日志 → 向用户显示友好错误 → 不阻止 Excel 启动（降级运行）
```

## 2. AutoOpen 完整实现

```csharp
public class AutoOpen : IExcelAddIn
{
    private static IServiceProvider _serviceProvider;

    public void AutoOpen()
    {
        // 第 1 步：捕获线程
        ExcelThreadDispatcher.CaptureExcelThread();

        // 第 2 步：日志最先初始化
        LogConfigurator.Initialize();

        try
        {
            // 第 3 步：DI 容器
            _serviceProvider = ConfigureServices();

            // 第 4 步：健康检查（不阻塞，异步执行）
            Task.Run(async () =>
            {
                var healthCheck = _serviceProvider.GetRequiredService<IHealthCheckService>();
                var results = await healthCheck.RunAllAsync();

                foreach (var r in results.Where(r => !r.IsHealthy))
                {
                    LogManager.GetCurrentClassLogger().Warn(
                        $"健康检查失败 [{r.Name}]: {r.ErrorMessage}");
                }

                // 将结果发布到 EventBus，让 TaskPane 显示
                var eventBus = _serviceProvider.GetRequiredService<IEventBus>();
                eventBus.Publish(new HealthCheckCompletedEvent(results));
            });

            // 第 5 步：数据库迁移（同步执行，必须完成才能用 DAL）
            var migrator = _serviceProvider.GetRequiredService<DatabaseMigrator>();
            migrator.RunPendingMigrations();

            // 第 6 步：注册 Excel 事件
            RegisterExcelEvents();

            LogManager.GetCurrentClassLogger().Info("BomAddIn 启动完成");
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Fatal(ex, "AutoOpen 致命错误");

            // 友好提示（仅在非静默模式下弹窗）
            MessageBox.Show(
                $"BOM 插件启动失败：{ex.Message}\n\n" +
                "Excel 将继续运行，但插件功能不可用。\n" +
                "请运行 BomAddIn.Diagnostic.exe 检查环境配置。",
                "BomAddIn 启动错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public void AutoClose()
    {
        // 1. 通知 WPF 仪表盘关闭
        DashboardBootstrapper.Close();

        // 2. 刷新缓存到 SQLite
        var cacheProvider = _serviceProvider?.GetService<ISqliteCacheProvider>();
        cacheProvider?.FlushToDisk();

        // 3. 释放 DI 容器
        (_serviceProvider as IDisposable)?.Dispose();

        LogManager.GetCurrentClassLogger().Info("BomAddIn 已关闭");
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // --- Infrastructure (Singleton) ---
        services.AddSingleton<IConfigProvider, AppConfigProvider>();
        services.AddSingleton<IEncryptionProvider, DpapiEncryptionProvider>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IAuditLogger, AuditInterceptor>();
        services.AddSingleton<INetworkMonitor, NetworkMonitor>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();

        // --- Bridge (Singleton) ---
        services.AddSingleton<IExcelThreadDispatcher, ExcelThreadDispatcher>();
        services.AddSingleton<IVersionAdapter, VersionAdapter>();

        // --- EventBus (Singleton) ---
        services.AddSingleton<IEventBus, ExcelEventBus>();

        // --- Data (Singleton for factories, Scoped for repositories) ---
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<ICacheProvider, MemoryCacheProvider>();
        services.AddSingleton<ISqliteCacheProvider, SqliteCacheProvider>();
        services.AddSingleton<DatabaseMigrator>();

        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IBomRepository, BomRepository>();
        services.AddScoped<IPriceRepository, PriceRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        // ... 其余 Repository

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // --- Core (Scoped per operation) ---
        services.AddScoped<IBomService, BomService>();
        services.AddScoped<IVarianceService, VarianceService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISyncService, SyncService>();

        services.AddScoped<IVarianceCalculator, VarianceCalculator>();
        services.AddScoped<IAlertEvaluator, AlertEvaluator>();

        return services.BuildServiceProvider();
    }
}
```

## 3. UDF 如何获取 DI 服务

```csharp
// Container.cs — 全局服务定位器（仅 UDF 使用）
//
// ⚠️ 这是服务定位器反模式，仅用于 UDF ——
//    Excel-DNA 无法通过构造函数注入 UDF 类
//    UI 代码仍应使用构造函数注入

public static class Container
{
    private static IServiceProvider _provider;

    public static void Initialize(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static T Resolve<T>() where T : class
    {
        if (_provider == null)
            throw new InvalidOperationException("Container 未初始化。请确保 AutoOpen 已调用。");

        // Scoped 服务：创建新 Scope（模拟 per-UDF-call）
        if (IsScopedService<T>())
        {
            using var scope = _provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        return _provider.GetRequiredService<T>();
    }
}
```

```csharp
// UDF 中使用
[ExcelFunction(Name = "BOMEXPAND", IsThreadSafe = true)]
public static object[,] BomExpand(string itemCode)
{
    var service = Container.Resolve<IBomService>();  // 通过定位器获取
    return service.Expand(itemCode).ToArray2D();
}
```

## 4. 数据库迁移：DbUp 集成

```csharp
public class DatabaseMigrator
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public void RunPendingMigrations()
    {
        using var connection = _connectionFactory.CreateConnection();

        // 确保 DbUp SchemaVersions 表存在
        EnsureDatabase.For.SqlDatabase(connection);

        var upgrader = DeployChanges.To
            .SqlDatabase(connection.ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.StartsWith("BomAddIn.Data.Migrations.S"))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            _logger.Fatal(result.Error, "数据库迁移失败");
            throw new InvalidOperationException(
                "数据库迁移失败。请检查 BomAddIn.Diagnostic.exe 了解详情。", result.Error);
        }

        _logger.Info($"数据库迁移完成。执行了 {result.Scripts.Count()} 个脚本。");
    }
}
```

**迁移脚本作为嵌入式资源**:
- 放在 `src/BomAddIn.Data/Migrations/` 目录
- `.csproj` 中: `<EmbeddedResource Include="Migrations\*.sql" />`
- 脚本命名: `S001_InitialSchema.sql`, `S002_SeedMaterials.sql`

## 5. 启动健康检查

```csharp
public interface IHealthCheck
{
    string Name { get; }
    Task<HealthCheckResult> CheckAsync();
}

public class HealthCheckResult
{
    public string Name { get; set; }
    public bool IsHealthy { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}

// 示例检查
public class DatabaseHealthCheck : IHealthCheck
{
    public string Name => "数据库连接";

    public async Task<HealthCheckResult> CheckAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            conn.Close();
            return new HealthCheckResult { Name = Name, IsHealthy = true, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Name = Name,
                IsHealthy = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }
}
```

| 检查项 | 用途 | 失败后行为 |
|--------|------|-----------|
| 数据库连接 | 确认 SQL Server 可达 | 标记离线模式 |
| SQLite 读写 | 确认缓存目录可写 | 禁用离线缓存 |
| ERP API 端点 | 确认网络+认证 | 显示"同步不可用" |
| Excel 版本 | 探测兼容性 | 降级动态数组功能 |
| .NET 运行时 | 确认无缺失程序集 | 报错退出 |

## 6. 自检清单

- [ ] `AutoOpen()` 不超过 3 秒（健康检查异步执行，不阻塞启动）
- [ ] 任何启动步骤失败都有日志 + 用户友好的降级
- [ ] DI 容器在 `AutoClose()` 中正确释放
- [ ] 迁移脚本作为嵌入式资源（不依赖外部文件路径）
- [ ] `Container` 服务定位器仅用于 UDF（UI 使用构造函数注入）
- [ ] NLog 在所有其他组件之前初始化
- [ ] Excel 关闭事件中保存了 SQLite 缓存

## 7. 参考

- [Extensibility.ExcelDNA.Sample: 完整 DI 集成示例](https://github.com/terryaney/Extensibility.ExcelDNA.Sample)
- [Excel-DNA/Samples: 官方启动引导示例](https://github.com/Excel-DNA/Samples)
- [DbUp 官方文档](https://dbup.readthedocs.io/)
