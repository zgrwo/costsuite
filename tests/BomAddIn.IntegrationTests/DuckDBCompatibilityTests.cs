using System;
using System.Data;
using System.Linq;
using BomAddIn.Data.Analysis;
using BomAddIn.Infrastructure.Models;
using Dapper;
using DuckDB.NET.Data;
using Xunit;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// DuckDB 原生库兼容性验证 — 推送前检查。
/// 验证下载的 duckdb.dll (v1.5.4) 与 DuckDB.NET.Data NuGet (v1.0.2) 兼容。
/// </summary>
public class DuckDBCompatibilityTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;

    public DuckDBCompatibilityTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DuckDB_Connection_OpensSuccessfully()
    {
        using var conn = new DuckDBConnection("DataSource=:memory:");
        conn.Open();
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public void DuckDB_Execute_CreateTableAndQuery()
    {
        using var conn = new DuckDBConnection("DataSource=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE test(id INTEGER, name VARCHAR); INSERT INTO test VALUES (1, 'hello');";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT COUNT(*) FROM test";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public void DuckDB_LoadFromSqlite_CompletesSuccessfully()
    {
        // 种子数据（按 FK 引用顺序：先 Suppliers，再 Materials，再 Prices/BomStructures）
        using var sqliteConn = _fixture.CreateConnection();
        sqliteConn.Execute("INSERT INTO Suppliers (OrgId, Code, Name) VALUES (1, 'SUP-001', 'TestSupplier')");
        var supId = sqliteConn.ExecuteScalar<long>("SELECT MAX(Id) FROM Suppliers");

        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'MAT-001', 'Test', 'SPEC', 'PCS', 'Raw', 1)");
        var matId = sqliteConn.ExecuteScalar<long>("SELECT MAX(Id) FROM Materials");

        // 第二条物料作为子节点
        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'MAT-002', 'Child', 'SPEC', 'PCS', 'Raw', 1)");
        var childId = sqliteConn.ExecuteScalar<long>("SELECT MAX(Id) FROM Materials");

        sqliteConn.Execute(@"INSERT INTO BomStructures (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Level, ValidFrom, VersionState)
            VALUES (1, @P, @C, 2, 1, '2026-01-01', 'Released')",
            new { P = matId, C = childId });

        sqliteConn.Execute(@"INSERT INTO Prices (OrgId, MaterialId, SupplierId, UnitPrice, Currency, EffectiveDate)
            VALUES (1, @M, @S, 100, 'CNY', '2026-01-01')",
            new { M = matId, S = supId });

        var provider = new BomAnalysisProvider();
        provider.LoadFromSqlite(sqliteConn);

        var nodes = provider.ExpandBom("MAT-001");
        Assert.NotEmpty(nodes);
        Assert.Equal("MAT-001", nodes[0].ItemCode);
        Assert.Equal(0, nodes[0].Level);
    }

    [Fact]
    public void DuckDB_ExpandBom_DiamondDedup_PreventsPathExplosion()
    {
        // 菱形 BOM: A→B→D, A→C→D  (D 被两个父节点共享)
        // 修复前: CTE 枚举 A→B→D 和 A→C→D 两条路径，D 的子节点被展开 2 次，逐层放大
        // 修复后: 全局去重 — D 只在首次出现时展开，O(N) 替代 O(分支^深度)
        using var sqliteConn = _fixture.CreateConnection();

        // 创建 4 个测试物料
        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'DIAMOND-A', 'A', '', 'PCS', '', 1)");
        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'DIAMOND-B', 'B', '', 'PCS', '', 1)");
        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'DIAMOND-C', 'C', '', 'PCS', '', 1)");
        sqliteConn.Execute("INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES (1, 'DIAMOND-D', 'D', '', 'PCS', '', 1)");

        var ids = sqliteConn.Query<long>("SELECT Id FROM Materials WHERE Code LIKE 'DIAMOND-%' ORDER BY Code").ToList();
        var aId = ids[0]; var bId = ids[1]; var cId = ids[2]; var dId = ids[3];

        // 构建菱形: A→B, A→C, B→D, C→D
        sqliteConn.Execute(@"INSERT INTO BomStructures (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Level, ValidFrom, VersionState)
            VALUES (1, @P, @C, 1, 1, '2026-01-01', 'Released')",
            new[] {
                new { P = aId, C = bId },
                new { P = aId, C = cId },
                new { P = bId, C = dId },
                new { P = cId, C = dId }
            });

        var provider = new BomAnalysisProvider();
        provider.LoadFromSqlite(sqliteConn);

        var nodes = provider.ExpandBom("DIAMOND-A");

        // 验证每个物料只出现一次（D 不会重复）
        var counts = nodes.GroupBy(n => n.ItemCode).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1, counts["DIAMOND-A"]);
        Assert.Equal(1, counts["DIAMOND-B"]);
        Assert.Equal(1, counts["DIAMOND-C"]);
        Assert.Equal(1, counts["DIAMOND-D"]);
        Assert.Equal(4, nodes.Count); // 总共 4 个唯一节点
    }

    [Fact]
    public void DuckDB_Version_IsUsable()
    {
        using var conn = new DuckDBConnection("DataSource=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version()";
        var version = cmd.ExecuteScalar()?.ToString();

        Assert.NotNull(version);
        Assert.StartsWith("v1.5", version);
    }
}
