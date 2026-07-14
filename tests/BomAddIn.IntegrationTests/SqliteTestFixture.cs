using System;
using System.Data;
using System.Data.SQLite;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Migration;

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
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.AuditAction>());
        Dapper.SqlMapper.AddTypeHandler(new BomAddIn.Infrastructure.Config.EnumStringTypeHandler<BomAddIn.Infrastructure.Models.Enums.SnapshotType>());
    }

    public SqliteTestFixture()
    {
        // 使用临时文件数据库 — 比 :memory: 更可靠地跨连接共享
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bom_test_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbPath};Foreign Keys=False;";

        _sharedConnection = new SQLiteConnection(connStr);
        _sharedConnection.Open();

        // 使用生产级 DatabaseMigrator 创建 schema，替代硬编码 CREATE TABLE
        var migrator = new DatabaseMigrator(this);
        migrator.RunPendingMigrations();
    }

    public string ConnectionString => $"Data Source={_dbPath};Foreign Keys=False;";

    public IDbConnection CreateConnection()
    {
        var conn = new SQLiteConnection($"Data Source={_dbPath};Foreign Keys=False;");
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
}
