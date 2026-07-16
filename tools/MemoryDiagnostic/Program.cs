using System;
using System.Diagnostics;
using BomAddIn.Core.Services;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryDiagnostic
{
    /// <summary>
    /// 验证 UDF 内存问题的实测工具。
    /// 运行: dotnet run --project tools/MemoryDiagnostic
    /// </summary>
    public class Program
    {
        public static void Main()
        {
            // 探测数据库
            var exeDir = AppContext.BaseDirectory;
            var dir = exeDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = System.IO.Path.Combine(dir, "database");
                if (System.IO.Directory.Exists(candidate))
                {
                    SqliteConnectionFactory.ProjectDbRoot = candidate;
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            Console.WriteLine($"数据库根目录: {SqliteConnectionFactory.ProjectDbRoot ?? "(未找到)"}");
            Console.WriteLine($"DEV 数据库: {SqliteConnectionFactory.DevDatabasePath}");
            Console.WriteLine($"数据库存在: {System.IO.File.Exists(SqliteConnectionFactory.DevDatabasePath)}");
            Console.WriteLine();

            // ===== 验证#1: CreateConnection 每次都打开新连接 =====
            Console.WriteLine("=== 验证#1: CreateConnection 是否每次打开新连接 ===");
            var factory = new SqliteConnectionFactory("DEV");
            var conn1 = factory.CreateConnection();
            var conn2 = factory.CreateConnection();
            Console.WriteLine($"连接1 State: {conn1.State}, Hash: {conn1.GetHashCode()}");
            Console.WriteLine($"连接2 State: {conn2.State}, Hash: {conn2.GetHashCode()}");
            Console.WriteLine($"是不同连接实例: {!ReferenceEquals(conn1, conn2)}");
            Console.WriteLine($"连接已 Open: {conn1.State == System.Data.ConnectionState.Open}");
            conn1.Dispose();
            conn2.Dispose();
            Console.WriteLine();

            // ===== 验证#2: DI Scope 创建 BomService 实例数 =====
            Console.WriteLine("=== 验证#2: Scoped BomService 是否每次创建新实例 ===");
            var services = ServiceConfigurator.Configure();

            // 模拟 5 个 UDF 调用（每个创建新 scope）
            var bomServiceHashes = new HashSet<int>();
            var scopeCount = 5;

            for (int i = 0; i < scopeCount; i++)
            {
                using var scope = services.CreateScope();
                var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                bomServiceHashes.Add(bomService.GetHashCode());
            }

            Console.WriteLine($"创建了 {scopeCount} 个 Scope");
            Console.WriteLine($"BomService 实例数: {bomServiceHashes.Count} (期望 5 → 每个 scope 创建新实例)");
            Console.WriteLine();

            // ===== 验证#3: EnsureLoaded 连接浪费 =====
            Console.WriteLine("=== 验证#3: EnsureLoaded 中的无用连接创建 ===");
            var analysisProvider = services.GetRequiredService<IBomAnalysisProvider>() as BomAnalysisProvider;
            var cacheProvider = services.GetRequiredService<ICacheProvider>() as MemoryCacheProvider;

            // 预热: 触发首次 LoadFromSqlite
            Console.WriteLine("首次加载 DuckDB...");
            var sw = Stopwatch.StartNew();

            // 通过 BomService.Expand 触发 EnsureLoaded
            using (var scope = services.CreateScope())
            {
                var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                var nodes = bomService.Expand("MAT-000001");
                Console.WriteLine($"  首次展开: {nodes.Count} 个节点, 耗时 {sw.ElapsedMilliseconds}ms");
            }
            sw.Stop();

            // 现在模拟 100 次 UDF 调用（EnsureLoaded 已经 loaded）
            Console.WriteLine();
            Console.WriteLine("模拟 100 次 UDF 调用 (EnsureLoaded 已预热)...");
            sw.Restart();
            int totalConnectionsOpened = 0;

            for (int i = 0; i < 100; i++)
            {
                using var scope = services.CreateScope();
                var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                // BomService.Expand 内部会 CreateConnection → EnsureLoaded → 立即返回
                var nodes = bomService.Expand("MAT-000001");
                totalConnectionsOpened++; // 每次 Expand 都打开一个 SQLite 连接
            }
            sw.Stop();
            Console.WriteLine($"100 次调用完成, 耗时 {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"无意义 SQLite 连接打开次数: {totalConnectionsOpened}");
            Console.WriteLine($"平均每次: {sw.ElapsedMilliseconds / 100.0:F1}ms");
            Console.WriteLine();

            // ===== 验证#4: MemoryCache 实际大小 =====
            Console.WriteLine("=== 验证#4: 缓存增长测试 ===");
            Console.WriteLine("展开 100 个不同物料（每个放入缓存）...");

            // 先清缓存
            cacheProvider.RemoveByPrefix("bom_expand:");

            long memBefore = GC.GetTotalMemory(true);
            int totalNodes = 0;
            sw.Restart();

            for (int i = 1; i <= 100; i++)
            {
                var code = $"MAT-{i:D6}";
                using var scope = services.CreateScope();
                var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                try
                {
                    var nodes = bomService.Expand(code);
                    totalNodes += nodes.Count;
                }
                catch (InvalidOperationException)
                {
                    // 物料可能不存在
                }
            }
            sw.Stop();
            long memAfter = GC.GetTotalMemory(true);

            Console.WriteLine($"展开 100 个物料, 总节点数: {totalNodes}, 耗时 {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"内存增长 (GC后): {(memAfter - memBefore) / 1024.0 / 1024.0:F2} MB");
            Console.WriteLine();

            // ===== 验证#5: DuckDB LoadFromSqlite 内存快照 =====
            Console.WriteLine("=== 验证#5: DuckDB 加载前后内存对比 ===");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long beforeDuckDb = GC.GetTotalMemory(true);

            // 触发重新加载
            var sqliteConn = factory.CreateConnection();
            sw.Restart();
            analysisProvider.LoadFromSqlite(sqliteConn);
            sw.Stop();
            sqliteConn.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long afterDuckDb = GC.GetTotalMemory(true);

            Console.WriteLine($"DuckDB 加载耗时: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"托管内存增长: {(afterDuckDb - beforeDuckDb) / 1024.0 / 1024.0:F2} MB");
            Console.WriteLine($"当前进程总内存: {Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0:F2} MB");

            // 测量多线程锁竞争
            Console.WriteLine();
            Console.WriteLine("=== 验证#6: DuckDB lock 串行化 ===");
            Console.WriteLine("10 线程并发 ExpandBom...");

            sw.Restart();
            var tasks = new System.Threading.Tasks.Task[10];
            for (int t = 0; t < 10; t++)
            {
                var code = $"MAT-{t + 1:D6}";
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    using var scope = services.CreateScope();
                    var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                    try { bomService.Expand(code); } catch { }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            sw.Stop();
            Console.WriteLine($"10 并发完成: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"如果与非并发 (单线程循环) 时间相近 → 证明被锁串行化");

            Console.WriteLine();
            Console.WriteLine("=== 诊断完成 ===");
        }
    }
}
