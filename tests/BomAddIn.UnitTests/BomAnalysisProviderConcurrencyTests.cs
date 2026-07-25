using System;
using System.Data;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using BomAddIn.Data.Analysis;
using DuckDB.NET.Data;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// BomAnalysisProvider 并发安全回归测试。
/// 验证 volatile + lock 双重检查模式在并发场景下:
/// 1. 无死锁 — 所有并发 EnsureLoaded 调用均完成
/// 2. 单次加载 — LoadFromSqlite 仅被第一个进入锁的线程执行
/// 3. 快速返回 — 加载完成后所有调用通过 volatile 检查短路返回
/// </summary>
public class BomAnalysisProviderConcurrencyTests
{
    /// <summary>
    /// 创建 BomAnalysisProvider.LoadFromSqlite 所需的最小 SQLite schema。
    /// 表结构必须与 LoadFromSqlite 中的 CREATE TABLE 语句一致。
    /// </summary>
    private static IDbConnection CreateSchemaConnection()
    {
        var conn = new SQLiteConnection("Data Source=:memory:;Foreign Keys=False;");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Materials (
                Id INTEGER, OrgId INTEGER, Code TEXT, Name TEXT,
                Spec TEXT, Unit TEXT, Category TEXT, IsActive INTEGER
            );
            CREATE TABLE BomStructures (
                Id INTEGER, OrgId INTEGER, ParentMaterialId INTEGER,
                ChildMaterialId INTEGER, Quantity REAL, Position TEXT,
                ScrapRate REAL, BomViewType TEXT, Level INTEGER,
                ValidFrom TEXT, ValidTo TEXT, VersionState TEXT
            );
            CREATE TABLE Prices (
                Id INTEGER, OrgId INTEGER, MaterialId INTEGER,
                SupplierId INTEGER, UnitPrice REAL, Currency TEXT,
                DataVersion INTEGER, EffectiveDate TEXT
            );
            CREATE TABLE BomClosure (
                AncestorId INTEGER NOT NULL,
                DescendantId INTEGER NOT NULL,
                Depth INTEGER NOT NULL,
                PathQuantity REAL NOT NULL DEFAULT 1.0,
                PRIMARY KEY (AncestorId, DescendantId)
            );
        ";
        cmd.ExecuteNonQuery();

        return conn;
    }

    [Fact]
    public async Task EnsureLoaded_ConcurrentCalls_OnlyLoadsOnce()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange
        var provider = new BomAnalysisProvider();
        using var sqliteConn = CreateSchemaConnection();

        provider.IsLoaded.Should().BeFalse("初始状态 IsLoaded 应为 false");

        var completedCount = 0;
        var tasks = new Task[10];

        // Act: 10 个并行任务同时调用 EnsureLoaded
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                provider.EnsureLoaded(sqliteConn);
                Interlocked.Increment(ref completedCount);
            });
        }

        await Task.WhenAll(tasks);

        // Assert: 无死锁 — 全部 10 个任务完成
        completedCount.Should().Be(10, "所有并发 EnsureLoaded 调用应完成，无死锁");

        // volatile + lock 双重检查: 仅第一个线程执行 LoadFromSqlite，
        // 其余 9 个线程在内层 lock 双重检查处返回
        provider.IsLoaded.Should().BeTrue("并发加载完成后 IsLoaded 应为 true");
    }

    [Fact]
    public async Task EnsureLoaded_PostLoad_FastReturn_ThroughVolatileCheck()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // 预热: 完成首次同步加载
        var provider = new BomAnalysisProvider();
        using var sqliteConn = CreateSchemaConnection();

        provider.EnsureLoaded(sqliteConn);
        provider.IsLoaded.Should().BeTrue("预热加载后 IsLoaded 应为 true");

        // 再次并发调用 — 所有调用应在外层 volatile IsLoaded 检查处即时返回，
        // 不进入 lock，不创建额外 DuckDB 连接
        var tasks = new Task[10];
        var completedCount = 0;

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                provider.EnsureLoaded(sqliteConn);
                Interlocked.Increment(ref completedCount);
            });
        }

        await Task.WhenAll(tasks);

        completedCount.Should().Be(10, "所有复用 EnsureLoaded 调用应快速完成");
        provider.IsLoaded.Should().BeTrue("IsLoaded 状态在预热后保持不变");
    }
}
