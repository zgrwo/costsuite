using System;
using System.Data;
using System.Data.SQLite;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;
using Dapper;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// SQLite 测试数据库夹具 — 通过 DatabaseMigrator 使用生产级迁移脚本创建 schema，
/// 确保测试 schema 与生产环境一致。
/// 实现 IDbConnectionFactory 供 Repository 使用。
/// </summary>
public class SqliteTestFixture : IDbConnectionFactory, IDisposable
{
    private readonly string _dbPath;
    private readonly SQLiteConnection _sharedConnection;

    static SqliteTestFixture()
    {
        // 注册 Dapper 枚举类型处理器，确保 TEXT 列 ↔ enum 正确映射
        // TT-5 fix: 与 ServiceConfigurator.Configure() 保持一致，注册全部 5 个枚举处理器
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.UserRole>());
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.VersionState>());
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.AuditAction>());
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.SnapshotType>());
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.SyncStatus>());
    }

    public SqliteTestFixture()
    {
        // 使用临时文件数据库 — 比 :memory: 更可靠地跨连接共享
        // TT-1 fix: Foreign Keys=True 与生产环境保持一致，确保测试能捕获 FK 违规
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bom_test_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbPath};Foreign Keys=True;";

        _sharedConnection = new SQLiteConnection(connStr);
        _sharedConnection.Open();

        // 使用生产级 DatabaseMigrator 创建 schema，替代硬编码 CREATE TABLE
        var migrator = new DatabaseMigrator(this);
        migrator.RunPendingMigrations();
    }

    public string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;";

    public IDbConnection CreateConnection()
    {
        var conn = new SQLiteConnection($"Data Source={_dbPath};Foreign Keys=True;");
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        _sharedConnection?.Close();
        _sharedConnection?.Dispose();

        // 清理临时数据库文件
        try
        {
            if (System.IO.File.Exists(_dbPath))
                System.IO.File.Delete(_dbPath);
        }
        catch
        {
            // 忽略清理错误
        }
    }

    /// <summary>插入测试用前置物料，返回其 Id。FK=True 时 BomVersions/Prices 等表需要前置 Materials。</summary>
    public long SeedMaterial(string code = "TEST-MAT", long? id = null)
    {
        using var conn = CreateConnection();
        var matId = id ?? DateTime.UtcNow.Ticks % 1000000;
        conn.Execute(
            "INSERT OR IGNORE INTO Materials (Id, OrgId, Code, Name, Unit, IsActive) VALUES (@Id, 1, @Code, @Name, 'PCS', 1)",
            new { Id = matId, Code = code, Name = $"Test Material {code}" });
        return matId;
    }

    /// <summary>插入测试用前置供应商，返回其 Id。</summary>
    public long SeedSupplier(string code = "TEST-SUP", long? id = null)
    {
        using var conn = CreateConnection();
        var supId = id ?? DateTime.UtcNow.Ticks % 1000000 + 1;
        conn.Execute(
            "INSERT OR IGNORE INTO Suppliers (Id, OrgId, Code, Name) VALUES (@Id, 1, @Code, @Name)",
            new { Id = supId, Code = code, Name = $"Test Supplier {code}" });
        return supId;
    }

    /// <summary>插入测试用前置用户，返回其 Id。</summary>
    public long SeedUser(string username = "testuser", long? id = null)
    {
        using var conn = CreateConnection();
        var userId = id ?? DateTime.UtcNow.Ticks % 1000000 + 2;
        conn.Execute(
            "INSERT OR IGNORE INTO Users (Id, Username, PasswordHash, Role, OrgId, IsActive) VALUES (@Id, @Username, 'hash', 'Admin', 1, 1)",
            new { Id = userId, Username = username });
        return userId;
    }

    /// <summary>插入测试用 BOM 边（自动创建父子物料），返回 BomStructures.Id。
    /// S007 后 BomVersions.BomId → BomStructures(Id)，版本类测试需以此为前置数据。</summary>
    public long SeedBomStructure()
    {
        var parent = SeedMaterial($"BOMP-{Guid.NewGuid():N}".Substring(0, 20));
        var child = SeedMaterial($"BOMC-{Guid.NewGuid():N}".Substring(0, 20));
        using var conn = CreateConnection();
        return conn.ExecuteScalar<long>(
            @"INSERT INTO BomStructures (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Level, ValidFrom, VersionState)
              VALUES (1, @Parent, @Child, 1.0, 1, '2020-01-01', 'Released');
              SELECT last_insert_rowid();",
            new { Parent = parent, Child = child });
    }
}
