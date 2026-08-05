using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Core.Services;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;
using Xunit;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// 连接生命周期集成测试 — 发版前 Max 审查回归测试 (P0: 连接 double-open)。
///
/// SqliteConnectionFactory.CreateConnection() 内部已 Open 连接（所有 Repository 依赖此契约）。
/// BomService/SyncService 写路径若在 CreateConnection() 之后再次调用 conn.Open()，
/// SQLiteConnection 将抛出 InvalidOperationException("The connection is already open.")。
///
/// 单元测试全部 mock 连接工厂，无法捕获此问题 — 本测试使用真实 SQLite 连接覆盖生产路径。
/// </summary>
public class ConnectionLifecycleTests
{
    /// <summary>IBomAnalysisProvider 测试桩 — 写路径测试不涉及 DuckDB 分析</summary>
    private sealed class StubAnalysisProvider : IBomAnalysisProvider
    {
        public bool IsLoaded => true;
        public List<BomExpandedNode> ExpandBom(string itemCode, DateTime? asOfDate = null) => new();
        public List<BomExpandedNode> ExpandBomViaClosure(string itemCode, DateTime? asOfDate = null) => new();
        public DataTable AggregatePrices(DateTime from, DateTime to) => new();
        public void LoadFromSqlite(IDbConnection sqliteConn) { }
        public void EnsureLoaded(IDbConnection sqliteConn) { }
    }

    private static BomService CreateBomService(SqliteTestFixture fixture)
    {
        return new BomService(
            new BomNodeRepository(fixture),
            new BomVersionRepository(fixture),
            new MaterialRepository(fixture),
            new StubAnalysisProvider(),
            new AuditService(new AuditLogRepository(fixture)),
            new MemoryCacheProvider(),
            new PriceRecordRepository(fixture),
            fixture,
            new AuthorizationService());
    }

    [Fact]
    public void CreateConnection_ReturnsOpenConnection()
    {
        // 契约验证：CreateConnection 返回的连接必须已打开（所有 Repository 依赖此契约）
        using var fixture = new SqliteTestFixture();
        using var conn = fixture.CreateConnection();
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public void BomService_AddNode_RealConnection_Succeeds()
    {
        // 回归: BomService.AddNode 使用 CreateConnection() 返回的已打开连接开启事务
        using var fixture = new SqliteTestFixture();
        var parent = fixture.SeedMaterial("PARENT-MAT");
        var child = fixture.SeedMaterial("CHILD-MAT");
        var service = CreateBomService(fixture);

        var node = new BomNode
        {
            OrgId = 1,
            ParentMaterialId = parent,
            ChildMaterialId = child,
            Quantity = 2.0,
            Level = 1,
            ValidFrom = DateTime.Today.AddDays(-1),
            VersionState = VersionState.Released,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = service.AddNode(node, UserRole.Admin, userId: 1);

        Assert.True(result.Id > 0);
        Assert.NotNull(service.GetById(result.Id));
    }

    [Fact]
    public void BomService_UpdateNode_RealConnection_Succeeds()
    {
        using var fixture = new SqliteTestFixture();
        var parent = fixture.SeedMaterial("PARENT-MAT");
        var child = fixture.SeedMaterial("CHILD-MAT");
        var service = CreateBomService(fixture);

        var node = new BomNode
        {
            OrgId = 1,
            ParentMaterialId = parent,
            ChildMaterialId = child,
            Quantity = 2.0,
            Level = 1,
            ValidFrom = DateTime.Today.AddDays(-1),
            VersionState = VersionState.Released,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        service.AddNode(node, UserRole.Admin, userId: 1);

        node.Quantity = 3.5;
        node.UpdatedAt = DateTime.UtcNow;
        service.UpdateNode(node, UserRole.Admin, userId: 1);

        var updated = service.GetById(node.Id);
        Assert.NotNull(updated);
        Assert.Equal(3.5, updated!.Quantity, 5);
    }

    [Fact]
    public void BomService_DeleteNode_RealConnection_Succeeds()
    {
        using var fixture = new SqliteTestFixture();
        var parent = fixture.SeedMaterial("PARENT-MAT");
        var child = fixture.SeedMaterial("CHILD-MAT");
        var service = CreateBomService(fixture);

        var node = new BomNode
        {
            OrgId = 1,
            ParentMaterialId = parent,
            ChildMaterialId = child,
            Quantity = 1.0,
            Level = 1,
            ValidFrom = DateTime.Today.AddDays(-1),
            VersionState = VersionState.Released,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        service.AddNode(node, UserRole.Admin, userId: 1);

        service.DeleteNode(node.Id, UserRole.Admin, userId: 1);

        Assert.Null(service.GetById(node.Id));
    }

    [Fact]
    public void BomVersion_Delete_NullifiesEstimatesReference()
    {
        // S007 Step 5b/7 回归：Estimates.BomVersionId 可空，直接删除版本时
        // 触发器 trg_BomVersions_Delete_Estimates_Nullify 将引用置 NULL，避免悬空引用
        using var fixture = new SqliteTestFixture();
        var bomId = fixture.SeedBomStructure();
        using var conn = fixture.CreateConnection();

        var versionId = conn.ExecuteScalar<long>(
            @"INSERT INTO BomVersions (BomId, VersionNumber, State, CreatedAt)
              VALUES (@BomId, 1, 'Released', datetime('now'));
              SELECT last_insert_rowid();",
            new { BomId = bomId });

        conn.Execute(
            "INSERT INTO Estimates (OrgId, BomVersionId, TotalCost, LaborHours) VALUES (1, @V, 100.0, 8.0)",
            new { V = versionId });

        conn.Execute("DELETE FROM BomVersions WHERE Id = @Id", new { Id = versionId });

        var estimateRef = conn.ExecuteScalar<long?>(
            "SELECT BomVersionId FROM Estimates WHERE TotalCost = 100.0");
        Assert.Null(estimateRef);
    }
}
