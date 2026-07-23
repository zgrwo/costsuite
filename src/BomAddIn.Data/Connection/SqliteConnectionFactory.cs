using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using BomAddIn.Infrastructure.Config;

namespace BomAddIn.Data.Connection
{
    /// <summary>SQLite 连接工厂 — DEV/PROD 环境隔离。
    /// 项目 database/ 目录结构: database/dev/ 和 database/prod/
    /// 未发现项目 database/ 时回退到 %LocalAppData%\BomAddIn\Data\</summary>
    public class SqliteConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly string _dbFileName;

        /// <summary>项目 database/ 根目录（由 BomAddIn 启动时根据 XLL 路径自动探测）。
        /// 设置后，DEV 使用 {ProjectDbRoot}/dev/，PROD 使用 {ProjectDbRoot}/prod/。</summary>
        public static string? ProjectDbRoot { get; set; }

        /// <summary>PROD 默认（用于 Diagnostic 工具等无 DI 场景）</summary>
        public SqliteConnectionFactory() : this("PROD") { }

        /// <summary>指定环境名创建</summary>
        public SqliteConnectionFactory(string environment)
        {
            _dbFileName = environment == "DEV"
                ? EnvironmentManager.DevDatabaseFileName
                : EnvironmentManager.ProdDatabaseFileName;
            _dbPath = GetDatabaseFilePath(environment, _dbFileName);
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Journal Mode=WAL;Busy Timeout=5000;";
        }

        /// <summary>从 EnvironmentManager 创建（DI 注入）</summary>
        public SqliteConnectionFactory(EnvironmentManager envManager)
        {
            _dbFileName = envManager.DatabaseFileName;
            _dbPath = GetDatabaseFilePath(envManager.Current, _dbFileName);
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Journal Mode=WAL;Busy Timeout=5000;";
        }

        public IDbConnection CreateConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public string ConnectionString => _connectionString;

        /// <summary>数据库文件完整路径</summary>
        public string DatabaseFilePath => _dbPath;

        /// <summary>数据库目录：项目 database/{env}/ 优先，否则 %LocalAppData%</summary>
        public static string GetDatabaseDirectory(string environment)
        {
            if (!string.IsNullOrWhiteSpace(ProjectDbRoot))
            {
                var envDir = Path.Combine(ProjectDbRoot, environment.ToLowerInvariant());
                Directory.CreateDirectory(envDir);
                return envDir;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = Path.Combine(appData, "BomAddIn", "Data");
            Directory.CreateDirectory(dbDir);
            return dbDir;
        }

        /// <summary>指定环境的数据库文件完整路径</summary>
        public static string GetDatabaseFilePath(string environment, string fileName)
        {
            return Path.Combine(GetDatabaseDirectory(environment), fileName);
        }

        /// <summary>生产数据库文件路径（固定）</summary>
        public static string ProdDatabasePath => GetDatabaseFilePath("PROD", "bom_data.sqlite");

        /// <summary>开发数据库文件路径（固定）</summary>
        public static string DevDatabasePath => GetDatabaseFilePath("DEV", "bom_data_dev.sqlite");
    }
}
