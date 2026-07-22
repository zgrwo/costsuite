using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;
using Dapper;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// M-4b: 数据库迁移升级测试。
/// 覆盖: 嵌入式脚本发现、迁移幂等性、Schema 完整性、迁移版本号顺序。
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MigrationTestFactory _factory;

    public MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bom_migration_test_{Guid.NewGuid():N}.db");
        _factory = new MigrationTestFactory(_dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch { /* 清理失败不阻止 */ }
    }

    [Fact]
    public void RunPendingMigrations_FirstRun_CreatesAllTables()
    {
        var migrator = new DatabaseMigrator(_factory);
        migrator.RunPendingMigrations();

        // 验证核心表存在
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();

        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").ToList();

        tables.Should().Contain("Materials");
        tables.Should().Contain("BomStructures");
        tables.Should().Contain("BomVersions");
        tables.Should().Contain("Prices");
        tables.Should().Contain("Inventories");
        tables.Should().Contain("Users");
        tables.Should().Contain("AppConfig");
        tables.Should().Contain("SyncLogs");
        tables.Should().Contain("AuditLogs");
    }

    [Fact]
    public void RunPendingMigrations_SecondRun_IsIdempotent()
    {
        var migrator = new DatabaseMigrator(_factory);

        // 第一次迁移
        migrator.RunPendingMigrations();

        // 第二次迁移不应抛异常（DbUp 幂等性）
        var act = () => migrator.RunPendingMigrations();
        act.Should().NotThrow();
    }

    [Fact]
    public void RunPendingMigrations_AfterMigration_SchemaVersionTableExists()
    {
        var migrator = new DatabaseMigrator(_factory);
        migrator.RunPendingMigrations();

        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();

        // DbUp 创建 SchemaVersions 表追踪已应用脚本
        var hasVersionTable = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SchemaVersions'");
        hasVersionTable.Should().Be(1);

        // 应该有 >= 5 条迁移记录 (S001~S005)
        var scriptCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SchemaVersions");
        scriptCount.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public void RunPendingMigrations_MaterialsTable_HasExpectedColumns()
    {
        var migrator = new DatabaseMigrator(_factory);
        migrator.RunPendingMigrations();

        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();

        var columns = conn.Query<string>(
            "SELECT name FROM pragma_table_info('Materials')").ToList();

        // S001 定义的核心列
        columns.Should().Contain("Id");
        columns.Should().Contain("OrgId");
        columns.Should().Contain("Code");
        columns.Should().Contain("Name");
        columns.Should().Contain("Spec");
        columns.Should().Contain("Unit");
        columns.Should().Contain("Category");
        columns.Should().Contain("IsActive");
    }

    [Fact]
    public void RunPendingMigrations_ApprovalWorkflow_HasBomVersionsTable()
    {
        // S002 审批工作流迁移验证
        var migrator = new DatabaseMigrator(_factory);
        migrator.RunPendingMigrations();

        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();

        var columns = conn.Query<string>(
            "SELECT name FROM pragma_table_info('BomVersions')").ToList();

        columns.Should().Contain("State");
        columns.Should().Contain("ApprovedBy");
        columns.Should().Contain("ApprovedAt");
        columns.Should().Contain("BomId");
    }

    /// <summary>
    /// 轻量 IDbConnectionFactory — 为迁移测试提供临时 SQLite 连接。
    /// </summary>
    private class MigrationTestFactory : IDbConnectionFactory
    {
        private readonly string _dbPath;
        public MigrationTestFactory(string dbPath) => _dbPath = dbPath;
        public string ConnectionString => $"Data Source={_dbPath}";
        public System.Data.IDbConnection CreateConnection()
        {
            var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            return conn;
        }
    }
}
