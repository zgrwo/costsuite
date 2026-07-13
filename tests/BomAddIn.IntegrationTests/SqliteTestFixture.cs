using System;
using System.Data;
using System.Data.SQLite;
using BomAddIn.Data.Connection;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// SQLite 内存数据库测试夹具 — 使用共享的 :memory: 连接。
/// 实现 IDbConnectionFactory 供 Repository 使用。
/// </summary>
public class SqliteTestFixture : IDbConnectionFactory, IDisposable
{
    private readonly string _dbPath;
    private readonly SQLiteConnection _sharedConnection;

    public SqliteTestFixture()
    {
        // 使用临时文件数据库 — 比 :memory: 更可靠地跨连接共享
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bom_test_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbPath};Foreign Keys=True;";

        _sharedConnection = new SQLiteConnection(connStr);
        _sharedConnection.Open();

        // 创建测试 schema
        ExecuteMigration(_sharedConnection);
    }

    public string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;";

    public IDbConnection CreateConnection()
    {
        var conn = new SQLiteConnection($"Data Source={_dbPath};Foreign Keys=True;");
        conn.Open();
        return conn;
    }

    private static void ExecuteMigration(IDbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Materials (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Spec TEXT,
                Unit TEXT DEFAULT 'pcs',
                Category TEXT,
                IsActive INTEGER DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE BomStructures (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                ParentMaterialId INTEGER NOT NULL,
                ChildMaterialId INTEGER NOT NULL,
                Quantity REAL NOT NULL,
                Position TEXT,
                ScrapRate REAL DEFAULT 0,
                BomViewType TEXT DEFAULT 'EBOM',
                Level INTEGER DEFAULT 1,
                ValidFrom TEXT,
                ValidTo TEXT,
                VersionState TEXT DEFAULT 'Released',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE Prices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                MaterialId INTEGER NOT NULL,
                SupplierId INTEGER,
                UnitPrice REAL NOT NULL,
                Currency TEXT DEFAULT 'CNY',
                DataVersion TEXT DEFAULT '1',
                EffectiveDate TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Role TEXT DEFAULT 'Viewer',
                OrgId INTEGER DEFAULT 1,
                IsActive INTEGER DEFAULT 1,
                FailedLoginAttempts INTEGER DEFAULT 0,
                LockoutUntil TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                LastLoginAt TEXT
            );

            CREATE TABLE BomVersions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BomId INTEGER NOT NULL,
                VersionNumber INTEGER NOT NULL DEFAULT 1,
                State TEXT NOT NULL DEFAULT 'Draft',
                ApprovedBy INTEGER,
                ApprovedAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE AuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER,
                Action TEXT NOT NULL,
                TableName TEXT NOT NULL,
                RecordId INTEGER,
                OldValues TEXT,
                NewValues TEXT,
                Timestamp TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE DataSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotType TEXT DEFAULT 'Manual',
                SnapshotData TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                Description TEXT
            );

            CREATE TABLE AppConfig (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT UNIQUE NOT NULL,
                Value TEXT,
                Description TEXT,
                UpdatedAt TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE Inventories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                MaterialId INTEGER NOT NULL,
                WarehouseId TEXT,
                Quantity REAL DEFAULT 0,
                DataVersion TEXT DEFAULT '1',
                SnapshotDate TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE Suppliers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                Code TEXT NOT NULL,
                Name TEXT,
                Contact TEXT,
                Rating REAL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                MaterialId INTEGER NOT NULL,
                OrderQty REAL DEFAULT 0,
                DueDate TEXT,
                DataVersion TEXT DEFAULT '1',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE Capacities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrgId INTEGER NOT NULL,
                WorkCenterId TEXT,
                CapacityHours REAL DEFAULT 0,
                DataVersion TEXT DEFAULT '1',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );
        ";
        cmd.ExecuteNonQuery();
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
