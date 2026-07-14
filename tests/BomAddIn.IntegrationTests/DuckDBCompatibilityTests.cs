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
