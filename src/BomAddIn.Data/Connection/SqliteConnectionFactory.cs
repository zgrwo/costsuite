using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using BomAddIn.Infrastructure.Config;

namespace BomAddIn.Data.Connection
{
    /// <summary>SQLite 连接工厂 — DEV/PROD 环境隔离，数据库文件在 LocalAppData</summary>
    public class SqliteConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly string _dbFileName;

        /// <summary>PROD 默认（用于 Diagnostic 工具等无 DI 场景）</summary>
        public SqliteConnectionFactory() : this("PROD") { }

        /// <summary>指定环境名创建</summary>
        public SqliteConnectionFactory(string environment)
        {
            _dbFileName = environment == "DEV"
                ? EnvironmentManager.DevDatabaseFileName
                : EnvironmentManager.ProdDatabaseFileName;
            _dbPath = GetDatabaseFilePath(_dbFileName);
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Journal Mode=WAL;Busy Timeout=5000;";
        }

        /// <summary>从 EnvironmentManager 创建（DI 注入）</summary>
        public SqliteConnectionFactory(EnvironmentManager envManager)
        {
            _dbFileName = envManager.DatabaseFileName;
            _dbPath = GetDatabaseFilePath(_dbFileName);
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Journal Mode=WAL;Busy Timeout=5000;";
        }

        public IDbConnection CreateConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public string ConnectionString => _connectionString;

        /// <summary>DbUp 兼容连接字符串 — 仅 Data Source=</summary>
        public string DbUpConnectionString => $"Data Source={_dbPath}";

        /// <summary>数据库文件完整路径</summary>
        public string DatabaseFilePath => _dbPath;

        /// <summary>数据库目录</summary>
        public static string GetDatabaseDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = Path.Combine(appData, "BomAddIn", "Data");
            Directory.CreateDirectory(dbDir);
            return dbDir;
        }

        /// <summary>指定文件名的完整数据库路径</summary>
        public static string GetDatabaseFilePath(string fileName)
        {
            return Path.Combine(GetDatabaseDirectory(), fileName);
        }

        /// <summary>生产数据库文件路径（固定）</summary>
        public static string ProdDatabasePath => GetDatabaseFilePath("bom_data.sqlite");

        /// <summary>开发数据库文件路径（固定）</summary>
        public static string DevDatabasePath => GetDatabaseFilePath("bom_data_dev.sqlite");
    }
}
