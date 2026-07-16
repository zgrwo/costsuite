using System;
using System.Data.SQLite;
using System.IO;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;

namespace BomAddIn.Diagnostic;

/// <summary>
/// BomAddIn 环境诊断 & 数据管理工具
/// 用法:
///   BomAddIn.Diagnostic.exe                     — 环境检查
///   BomAddIn.Diagnostic.exe seed                — 生成种子数据 (默认 1万+5万)
///   BomAddIn.Diagnostic.exe seed 1000 5000      — 生成指定规模数据
///   BomAddIn.Diagnostic.exe stats               — 显示数据库统计
///   BomAddIn.Diagnostic.exe env                 — 查看当前环境
///   BomAddIn.Diagnostic.exe env dev             — 切换到 DEV 环境
///   BomAddIn.Diagnostic.exe env prod            — 切换到 PROD 环境
///   BomAddIn.Diagnostic.exe sync-prod-to-dev    — PROD→DEV 单向同步（需确认）
///   BomAddIn.Diagnostic.exe sync-dev-to-prod    — DEV→PROD 单向同步（需确认 + 强制备份）
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        // 探测项目 database/ 目录
        DetectProjectDatabase();

        Console.WriteLine("=== BOM Suite Diagnostic Tool v1.1 ===");
        Console.WriteLine();

        var command = args.Length > 0 ? args[0].ToLower() : "check";

        switch (command)
        {
            case "seed":
                RunSeedData(args);
                break;
            case "stats":
                ShowStats();
                break;
            case "env":
                RunEnvCommand(args);
                break;
            case "sync-prod-to-dev":
                RunSyncProdToDev(args);
                break;
            case "sync-dev-to-prod":
                RunSyncDevToProd(args);
                break;
            default:
                RunDiagnostic();
                break;
        }

        return 0;
    }

    /// <summary>读取当前环境并创建对应连接工厂</summary>
    /// <summary>探测项目 database/ 目录：从 exe 位置向上查找</summary>
    private static void DetectProjectDatabase()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var dir = exeDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, "database");
                if (Directory.Exists(candidate))
                {
                    SqliteConnectionFactory.ProjectDbRoot = candidate;
                    return;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { /* 探测失败，使用 %LocalAppData% 回退 */ }
    }

    private static SqliteConnectionFactory CreateEnvFactory()
    {
        var env = ReadCurrentEnvironment();
        return new SqliteConnectionFactory(env);
    }

    /// <summary>从 AppConfig 表读取当前环境名</summary>
    private static string ReadCurrentEnvironment()
    {
        try
        {
            var prodDb = SqliteConnectionFactory.ProdDatabasePath;
            if (!File.Exists(prodDb)) return "PROD";

            using var conn = new SQLiteConnection($"Data Source={prodDb}");
            conn.Open();
            var env = conn.QueryFirstOrDefault<string>(
                "SELECT Value FROM AppConfig WHERE Key = 'Environment:Current'");
            return !string.IsNullOrWhiteSpace(env) ? env : "PROD";
        }
        catch
        {
            return "PROD";
        }
    }

    private static void RunDiagnostic()
    {
        Console.WriteLine("[OK] .NET Runtime: " + Environment.Version);
        Console.WriteLine("[OK] OS: " + Environment.OSVersion);
        Console.WriteLine($"[OK] 当前环境: {ReadCurrentEnvironment()}");

        // 1. 数据库目录写入权限检查
        try
        {
            var dbDir = Path.GetDirectoryName(SqliteConnectionFactory.ProdDatabasePath);
            if (!string.IsNullOrEmpty(dbDir) && Directory.Exists(dbDir))
            {
                var testFile = Path.Combine(dbDir, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                Console.WriteLine($"[OK] 数据库目录写入权限: {dbDir}");
            }
        }
        catch { Console.WriteLine("[!!] 数据库目录写入权限不足"); }

        // 2. ERP 端点网络可达性检查
        try
        {
            var prodDb = SqliteConnectionFactory.ProdDatabasePath;
            if (File.Exists(prodDb))
            {
                using var conn = new SQLiteConnection($"Data Source={prodDb}");
                conn.Open();
                var erpEndpoint = conn.QueryFirstOrDefault<string>(
                    "SELECT Value FROM AppConfig WHERE Key = 'Erp:Endpoint'");
                if (!string.IsNullOrWhiteSpace(erpEndpoint))
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var response = http.GetAsync(erpEndpoint).Result;
                    Console.WriteLine($"[OK] ERP 端点可达: {erpEndpoint} ({(int)response.StatusCode})");
                }
                else
                    Console.WriteLine("[  ] ERP 端点未配置");
            }
        }
        catch { Console.WriteLine("[!!] ERP 端点不可达"); }

        // 3. 配置文件完整性检查
        try
        {
            var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var configFiles = new[] { "NLog.config" };
            foreach (var f in configFiles)
            {
                var path = Path.Combine(exeDir ?? ".", f);
                if (File.Exists(path))
                    Console.WriteLine($"[OK] 配置文件存在: {f}");
                else
                    Console.WriteLine($"[  ] 配置文件缺失: {f}");
            }
        }
        catch { Console.WriteLine("[!!] 配置文件检查失败"); }

        try
        {
            var factory = CreateEnvFactory();
            using var conn = factory.CreateConnection();
            var materialCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Materials WHERE IsActive=1");
            var bomCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM BomStructures");
            var userCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Users");

            Console.WriteLine($"[OK] 数据库: {factory.ConnectionString}");
            Console.WriteLine($"     物料: {materialCount:N0}   BOM节点: {bomCount:N0}   用户: {userCount:N0}");

            if (materialCount == 0)
                Console.WriteLine("[  ] 提示: 数据库为空，运行 'BomAddIn.Diagnostic.exe seed' 生成测试数据。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 数据库错误: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("诊断完成。按任意键退出...");
        Console.ReadKey();
    }

    private static void RunSeedData(string[] args)
    {
        int materialCount = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 10000;
        int bomNodeCount = args.Length > 2 && int.TryParse(args[2], out var b) ? b : 50000;
        int months = args.Length > 3 && int.TryParse(args[3], out var h) ? h : 12;

        Console.WriteLine($"生成种子数据: {materialCount:N0} 物料 + {bomNodeCount:N0} BOM节点 + {months} 月历史");
        Console.WriteLine("正在生成...");

        try
        {
            var factory = CreateEnvFactory();
            Console.WriteLine("  [1/2] 初始化数据库...");
            var migrator = new DatabaseMigrator(factory);
            migrator.RunPendingMigrations();

            // 生成种子数据
            Console.WriteLine("  [2/2] 生成数据...");
            var generator = new SeedDataGenerator(factory, new AuthorizationService(),
                new BomAddIn.Infrastructure.Security.BCryptPasswordHasher());
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var result = generator.Generate(UserRole.Admin, materialCount, bomNodeCount, months);
            watch.Stop();

            if (result.Skipped)
            {
                Console.WriteLine("  [!] 数据库已有数据，跳过生成。");
            }
            else if (result.ErrorMessage != null)
            {
                Console.WriteLine($"  [!!] 错误: {result.ErrorMessage}");
            }
            else
            {
                Console.WriteLine($"  [OK] 完成! 耗时 {watch.Elapsed.TotalSeconds:F1}s");
                Console.WriteLine($"       物料:       {result.MaterialsCreated:N0}");
                Console.WriteLine($"       供应商:     {result.SuppliersCreated:N0}");
                Console.WriteLine($"       BOM节点:    {result.BomNodesCreated:N0}");
                Console.WriteLine($"       BOM版本:    {result.BomVersionsCreated:N0}");
                Console.WriteLine($"       价格记录:   {result.PriceRecordsCreated:N0}");
                Console.WriteLine($"       库存记录:   {result.InventoryRecordsCreated:N0}");
                Console.WriteLine($"       订单:       {result.OrdersCreated:N0}");
                Console.WriteLine($"       产能:       {result.CapacitiesCreated:N0}");
                Console.WriteLine($"       成本估算:   {result.EstimatesCreated:N0}");
                Console.WriteLine($"       同步日志:   {result.SyncLogsCreated:N0}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 失败: {ex.Message}");
        }

        Console.WriteLine();
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    private static void ShowStats()
    {
        try
        {
            var factory = CreateEnvFactory();
            using var conn = factory.CreateConnection();

            Console.WriteLine("=== 数据库统计 ===");
            Console.WriteLine($"路径: {factory.ConnectionString}");
            Console.WriteLine();

            var tables = new[] { "Materials", "BomStructures", "BomVersions", "Prices", "Inventories",
                                 "Orders", "Capacities", "Estimates", "Suppliers", "Users",
                                 "UserTokens", "AuditLogs", "SyncLogs", "AppConfig", "DataSnapshots" };

            foreach (var table in tables)
            {
                try
                {
                    var count = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
                    Console.WriteLine($"  {table,-20} {count,8:N0}");
                }
                catch
                {
                    // 表可能不存在
                }
            }

            Console.WriteLine();
            Console.Write("总行数: ");
            var total = 0L;
            foreach (var table in tables)
            {
                try { total += conn.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}"); } catch { }
            }
            Console.WriteLine($"{total:N0}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 错误: {ex.Message}");
        }

        Console.WriteLine();
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    private static void RunEnvCommand(string[] args)
    {
        var dbDir = SqliteConnectionFactory.GetDatabaseDirectory("PROD");
        var prodDb = SqliteConnectionFactory.ProdDatabasePath;

        // 读取当前环境
        var currentEnv = "PROD";
        try
        {
            using var conn = new SQLiteConnection($"Data Source={prodDb}");
            conn.Open();
            try
            {
                currentEnv = conn.QueryFirstOrDefault<string>(
                    "SELECT Value FROM AppConfig WHERE Key = 'Environment:Current'") ?? "PROD";
            }
            catch { /* 表可能不存在 */ }
        }
        catch { /* 数据库不可用 */ }

        if (args.Length < 2)
        {
            Console.WriteLine($"当前环境: {currentEnv}");
            Console.WriteLine($"PROD 数据库: {SqliteConnectionFactory.ProdDatabasePath}");
            Console.WriteLine($"DEV  数据库: {SqliteConnectionFactory.DevDatabasePath}");
            Console.WriteLine();
            Console.WriteLine("切换环境: BomAddIn.Diagnostic.exe env dev|prod");
            return;
        }

        var target = args[1].ToUpperInvariant();
        if (target != "DEV" && target != "PROD")
        {
            Console.WriteLine($"无效环境: {args[1]}。有效值: DEV, PROD");
            return;
        }

        if (target == currentEnv)
        {
            Console.WriteLine($"环境已是 {currentEnv}，无需切换。");
            return;
        }

        // 确保目标数据库存在
        var dbPath = target == "DEV" ? SqliteConnectionFactory.DevDatabasePath : SqliteConnectionFactory.ProdDatabasePath;
        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"目标数据库不存在: {dbPath}");
            Console.WriteLine("将创建空数据库。");
        }

        // 持久化环境设置
        try
        {
            using var conn = new SQLiteConnection($"Data Source={prodDb}");
            conn.Open();

            // 确保 AppConfig 表存在
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS AppConfig (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Key TEXT UNIQUE NOT NULL,
                    Value TEXT,
                    Description TEXT,
                    UpdatedAt TEXT DEFAULT (datetime('now'))
                )");

            conn.Execute(
                @"INSERT INTO AppConfig (Key, Value, Description, UpdatedAt)
                  VALUES ('Environment:Current', @Value, '运行环境', datetime('now'))
                  ON CONFLICT(Key) DO UPDATE SET Value=@Value, UpdatedAt=datetime('now')",
                new { Value = target });

            Console.WriteLine($"环境已切换: {currentEnv} → {target}");
            Console.WriteLine($"数据库路径: {dbPath}");
            Console.WriteLine();
            Console.WriteLine("⚠ 请重启 Excel 插件使环境切换生效。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 切换失败: {ex.Message}");
        }
    }

    private static void RunSyncProdToDev(string[] args)
    {
        var prodDb = SqliteConnectionFactory.ProdDatabasePath;
        var devDb = SqliteConnectionFactory.DevDatabasePath;

        Console.WriteLine("PROD→DEV 单向数据同步");
        Console.WriteLine($"  源 (PROD): {prodDb}");
        Console.WriteLine($"  目标 (DEV): {devDb}");
        Console.WriteLine();

        if (!File.Exists(prodDb))
        {
            Console.WriteLine("[!!] PROD 数据库不存在，无法同步。");
            return;
        }

        // 安全守卫: 确认
        var force = args.Length > 1 && args[1] == "--force";
        if (!force)
        {
            Console.Write("确认覆盖 DEV 数据库? (输入 yes 继续): ");
            var input = Console.ReadLine();
            if (input?.ToLower() != "yes")
            {
                Console.WriteLine("已取消。");
                return;
            }
        }

        try
        {
            // 备份现有 DEV 数据库
            if (File.Exists(devDb))
            {
                var backupPath = devDb.Replace(".sqlite", $"_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sqlite");
                File.Copy(devDb, backupPath);
                Console.WriteLine($"已备份 DEV 数据库: {backupPath}");
            }

            // 复制 PROD → DEV
            File.Copy(prodDb, devDb, overwrite: true);
            Console.WriteLine("同步完成 — PROD 数据已复制到 DEV。");
            Console.WriteLine();
            Console.WriteLine("提示: 运行 'BomAddIn.Diagnostic.exe env dev' 切换到 DEV 环境。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 同步失败: {ex.Message}");
        }
    }

    private static void RunSyncDevToProd(string[] args)
    {
        var prodDb = SqliteConnectionFactory.ProdDatabasePath;
        var devDb = SqliteConnectionFactory.DevDatabasePath;

        Console.WriteLine("=== DEV→PROD 单向数据迁移 ===");
        Console.WriteLine($"  源 (DEV):  {devDb}");
        Console.WriteLine($"  目标 (PROD): {prodDb}");
        Console.WriteLine();

        if (!File.Exists(devDb))
        {
            Console.WriteLine("[!!] DEV 数据库不存在，无法迁移。请先运行 'seed' 生成数据。");
            return;
        }

        // 安全守卫 1: 检查 DEV 数据库中有数据
        try
        {
            using var devConn = new SQLiteConnection($"Data Source={devDb}");
            devConn.Open();
            var materialCount = devConn.ExecuteScalar<int>("SELECT COUNT(*) FROM Materials");
            if (materialCount == 0)
            {
                Console.WriteLine("[!!] DEV 数据库为空，无法迁移。请先运行 'seed' 生成数据。");
                return;
            }
            Console.WriteLine($"DEV 数据库状态: {materialCount:N0} 物料");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 无法读取 DEV 数据库: {ex.Message}");
            return;
        }

        // 安全守卫 2: 双因子确认 (DEV→PROD 是高风险操作)
        var force = args.Length > 1 && args[1] == "--force";
        if (!force)
        {
            Console.WriteLine();
            Console.WriteLine("⚠⚠⚠  WARNING: 此操作将以 DEV 数据覆盖 PROD 数据库! ⚠⚠⚠");
            Console.WriteLine("   生产环境数据将被替换。");
            Console.WriteLine();
            Console.Write("确认将 DEV 数据迁移至 PROD? (输入 PROD 继续): ");
            var input = Console.ReadLine();
            if (input?.Trim().ToUpperInvariant() != "PROD")
            {
                Console.WriteLine("已取消。输入内容与 'PROD' 不匹配。");
                return;
            }
        }
        else
        {
            Console.WriteLine("⚠ --force: 跳过确认提示。");
        }

        try
        {
            // 备份现有 PROD 数据库（强制备份，不可跳过）
            if (File.Exists(prodDb))
            {
                var backupPath = prodDb.Replace(".sqlite", $"_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sqlite");
                File.Copy(prodDb, backupPath);
                Console.WriteLine($"✅ 已备份 PROD 数据库: {backupPath}");
            }

            // DEV → PROD 复制
            File.Copy(devDb, prodDb, overwrite: true);
            Console.WriteLine("✅ 迁移完成 — DEV 数据已发布到 PROD。");
            Console.WriteLine();
            Console.WriteLine("提示: 运行 'BomAddIn.Diagnostic.exe env prod' 确认切换到 PROD 环境。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!!] 迁移失败: {ex.Message}");
            Console.WriteLine("   PROD 备份文件未被修改，可手动恢复。");
        }
    }
}
