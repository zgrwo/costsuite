using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using BomAddIn.Data.Analysis;
using BomAddIn.Infrastructure.Models;
using DuckDB.NET.Data;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// BOM Closure Table 展开测试 — 验证边界场景：
/// 1. 菱形依赖（DAG 去重）
/// 2. 深链（>20 层截断）
/// 3. Closure Table 与 BFS 结果一致性
/// 4. Closure Table 为空时 fallback 到 BFS
/// 5. 循环引用安全性
/// </summary>
public class BomClosureTableTests
{
    /// <summary>创建包含 BomClosure 表的最小 SQLite schema</summary>
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

    private static void InsertMaterial(IDbConnection conn, long id, string code, string name, string unit = "PCS")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Materials (Id, OrgId, Code, Name, Spec, Unit, Category, IsActive) VALUES ($id, 1, $code, $name, '', $unit, 'Make', 1)";
        cmd.Parameters.Add(new SQLiteParameter("$id", id));
        cmd.Parameters.Add(new SQLiteParameter("$code", code));
        cmd.Parameters.Add(new SQLiteParameter("$name", name));
        cmd.Parameters.Add(new SQLiteParameter("$unit", unit));
        cmd.ExecuteNonQuery();
    }

    private static void InsertBomEdge(IDbConnection conn, long parentId, long childId, double qty = 1.0)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO BomStructures (Id, OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate, BomViewType, Level, ValidFrom, ValidTo, VersionState)
                            VALUES (NULL, 1, $p, $c, $q, '', 0, 'EBOM', 1, '2020-01-01', NULL, 'Released')";
        cmd.Parameters.Add(new SQLiteParameter("$p", parentId));
        cmd.Parameters.Add(new SQLiteParameter("$c", childId));
        cmd.Parameters.Add(new SQLiteParameter("$q", qty));
        cmd.ExecuteNonQuery();
    }

    private static void InsertClosureRow(IDbConnection conn, long ancestorId, long descendantId, int depth, double pathQty)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity) VALUES ($a, $d, $dep, $q)";
        cmd.Parameters.Add(new SQLiteParameter("$a", ancestorId));
        cmd.Parameters.Add(new SQLiteParameter("$d", descendantId));
        cmd.Parameters.Add(new SQLiteParameter("$dep", depth));
        cmd.Parameters.Add(new SQLiteParameter("$q", pathQty));
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void ExpandBomViaClosure_DiamondDag_ReturnsDeduplicatedNodes()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange: 菱形 A→B, A→C, B→D, C→D
        using var sqliteConn = CreateSchemaConnection();
        InsertMaterial(sqliteConn, 1, "MAT-A", "Root A");
        InsertMaterial(sqliteConn, 2, "MAT-B", "Child B");
        InsertMaterial(sqliteConn, 3, "MAT-C", "Child C");
        InsertMaterial(sqliteConn, 4, "MAT-D", "Leaf D");

        InsertBomEdge(sqliteConn, 1, 2, 2.0);  // A→B qty=2
        InsertBomEdge(sqliteConn, 1, 3, 3.0);  // A→C qty=3
        InsertBomEdge(sqliteConn, 2, 4, 1.5);  // B→D qty=1.5
        InsertBomEdge(sqliteConn, 3, 4, 2.0);  // C→D qty=2.0

        // Closure: A→B(1,2), A→C(1,3), A→D(2, 2*1.5+3*2=9), B→D(1,1.5), C→D(1,2)
        InsertClosureRow(sqliteConn, 1, 2, 1, 2.0);
        InsertClosureRow(sqliteConn, 1, 3, 1, 3.0);
        InsertClosureRow(sqliteConn, 1, 4, 2, 9.0);  // 聚合路径数量
        InsertClosureRow(sqliteConn, 2, 4, 1, 1.5);
        InsertClosureRow(sqliteConn, 3, 4, 1, 2.0);

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act
        var results = provider.ExpandBomViaClosure("MAT-A");

        // Assert
        results.Should().HaveCount(4, "菱形 DAG 中 D 只出现一次（去重）");
        results[0].ItemCode.Should().Be("MAT-A");
        results[0].Level.Should().Be(0);
        results[0].Quantity.Should().Be(1.0);

        // D 在 Closure 中聚合了路径数量
        var nodeD = results.First(n => n.ItemCode == "MAT-D");
        nodeD.Level.Should().Be(2);
        nodeD.Quantity.Should().Be(9.0, "PathQuantity = 2*1.5 + 3*2 = 9");
    }

    [Fact]
    public void ExpandBomViaClosure_EmptyClosure_FallsBackToBfs()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange: 简单链 A→B→C，Closure Table 为空
        using var sqliteConn = CreateSchemaConnection();
        InsertMaterial(sqliteConn, 1, "MAT-A", "Root A");
        InsertMaterial(sqliteConn, 2, "MAT-B", "Child B");
        InsertMaterial(sqliteConn, 3, "MAT-C", "Leaf C");

        InsertBomEdge(sqliteConn, 1, 2, 1.0);
        InsertBomEdge(sqliteConn, 2, 3, 1.0);
        // 不插入 BomClosure 数据

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act
        var results = provider.ExpandBomViaClosure("MAT-A");

        // Assert: fallback 到 BFS，结果应包含 3 个节点
        results.Should().HaveCount(3, "Closure 为空时 fallback BFS 仍应正确展开");
        results.Select(n => n.ItemCode).Should().ContainInOrder("MAT-A", "MAT-B", "MAT-C");
    }

    [Fact]
    public void ExpandBomViaClosure_DeepChain_RespectsMaxDepth()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange: 25 层深链 (超过 maxLevel=20)
        using var sqliteConn = CreateSchemaConnection();
        const int chainLength = 25;

        for (int i = 1; i <= chainLength; i++)
            InsertMaterial(sqliteConn, i, $"MAT-{i:D3}", $"Node {i}");

        for (int i = 1; i < chainLength; i++)
            InsertBomEdge(sqliteConn, i, i + 1, 1.0);

        // Closure: 只填充前 20 层（模拟触发器 depth<50 但 BFS maxLevel=20 的行为）
        for (int depth = 1; depth <= 20 && depth < chainLength; depth++)
            InsertClosureRow(sqliteConn, 1, depth + 1, depth, 1.0);

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act
        var results = provider.ExpandBomViaClosure("MAT-001");

        // Assert: Closure Table 返回预计算的 20 层 + 根节点 = 21
        results.Should().HaveCount(21, "Closure Table 返回预计算深度范围内的节点");
        results[0].Level.Should().Be(0);
        results.Last().Level.Should().Be(20);
    }

    [Fact]
    public void ExpandBomViaClosure_NonExistentMaterial_ReturnsEmpty()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        using var sqliteConn = CreateSchemaConnection();
        InsertMaterial(sqliteConn, 1, "MAT-A", "Root A");
        InsertClosureRow(sqliteConn, 1, 1, 0, 1.0);

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act
        var results = provider.ExpandBomViaClosure("NONEXIST");

        // Assert
        results.Should().BeEmpty("不存在的物料编码应返回空列表");
    }

    [Fact]
    public void ExpandBomViaClosure_ConsistentWithBfs_ForSimpleChain()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange: A→B(2)→C(3)
        using var sqliteConn = CreateSchemaConnection();
        InsertMaterial(sqliteConn, 1, "MAT-A", "Root A");
        InsertMaterial(sqliteConn, 2, "MAT-B", "Child B");
        InsertMaterial(sqliteConn, 3, "MAT-C", "Leaf C");

        InsertBomEdge(sqliteConn, 1, 2, 2.0);
        InsertBomEdge(sqliteConn, 2, 3, 3.0);

        // Closure: A→B(1,2), A→C(2,6), B→C(1,3)
        InsertClosureRow(sqliteConn, 1, 2, 1, 2.0);
        InsertClosureRow(sqliteConn, 1, 3, 2, 6.0);
        InsertClosureRow(sqliteConn, 2, 3, 1, 3.0);

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act
        var bfsResults = provider.ExpandBom("MAT-A");
        var closureResults = provider.ExpandBomViaClosure("MAT-A");

        // Assert: 节点数一致
        closureResults.Should().HaveCount(bfsResults.Count, "Closure 和 BFS 展开的节点数应一致");

        // 各层级物料编码一致
        closureResults.Select(n => n.ItemCode).Should().BeEquivalentTo(
            bfsResults.Select(n => n.ItemCode), "两种展开方式的节点集合应相同");

        // 累积数量一致
        foreach (var bfsNode in bfsResults)
        {
            var closureNode = closureResults.First(n => n.MaterialId == bfsNode.MaterialId);
            closureNode.Quantity.Should().BeApproximately(bfsNode.Quantity, 1e-9,
                $"物料 {bfsNode.ItemCode} 的累积数量应一致");
        }
    }

    [Fact]
    public void ExpandBom_CyclicBom_TerminatesWithVisitedSet()
    {
        // Skip if DuckDB native library is not available
        try { using var test = new DuckDBConnection("DataSource=:memory:"); test.Open(); }
        catch { return; }

        // Arrange: 循环 A→B→C→A（BFS 的 HashSet 去重应终止）
        using var sqliteConn = CreateSchemaConnection();
        InsertMaterial(sqliteConn, 1, "MAT-A", "Node A");
        InsertMaterial(sqliteConn, 2, "MAT-B", "Node B");
        InsertMaterial(sqliteConn, 3, "MAT-C", "Node C");

        InsertBomEdge(sqliteConn, 1, 2, 1.0);
        InsertBomEdge(sqliteConn, 2, 3, 1.0);
        InsertBomEdge(sqliteConn, 3, 1, 1.0);  // 循环回 A

        var provider = new BomAnalysisProvider();
        provider.EnsureLoaded(sqliteConn);

        // Act: BFS 展开
        var results = provider.ExpandBom("MAT-A");

        // Assert: 每个节点只出现一次（HashSet 去重）
        results.Should().HaveCount(3, "循环 BOM 中每个节点只展开一次");
        results.Select(n => n.ItemCode).Should().OnlyHaveUniqueItems("不应有重复节点");
    }
}
