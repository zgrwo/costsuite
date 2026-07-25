using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using BomAddIn.Infrastructure.Logging;
using Dapper;

namespace BomAddIn.Infrastructure.Config
{
    /// <summary>
    /// 环境管理器 — DEV/PROD 数据库隔离。
    /// 环境名持久化在 AppConfig 表中（key=Environment:Current），
    /// 默认 PROD。
    /// </summary>
    public class EnvironmentManager
    {
        private const string ConfigKey = "Environment:Current";
        private const string DefaultEnvironment = "PROD";

        public static readonly IReadOnlyList<string> ValidEnvironments =
            new List<string> { "DEV", "PROD" }.AsReadOnly();

        private readonly object _lock = new();
        private string _current = DefaultEnvironment;

        /// <summary>当前环境名（DEV 或 PROD）。线程安全。</summary>
        public string Current
        {
            get { lock (_lock) return _current; }
            private set { lock (_lock) _current = value; }
        }

        public bool IsProd => Current == "PROD";
        public bool IsDev => Current == "DEV";

        /// <summary>数据库文件名（不含路径）</summary>
        public string DatabaseFileName => IsProd ? "bom_data.sqlite" : "bom_data_dev.sqlite";

        /// <summary>PROD 数据库文件名</summary>
        public static string ProdDatabaseFileName => "bom_data.sqlite";

        /// <summary>DEV 数据库文件名</summary>
        public static string DevDatabaseFileName => "bom_data_dev.sqlite";

        /// <summary>
        /// 从 AppConfig 表加载当前环境设置。
        /// 由 ServiceConfigurator 在容器构建后调用。
        /// </summary>
        public void LoadFromDb(string connectionString)
        {
            try
            {
                using var conn = new SQLiteConnection(connectionString);
                conn.Open();
                var value = conn.QueryFirstOrDefault<string>(
                    "SELECT Value FROM AppConfig WHERE Key = @Key",
                    new { Key = ConfigKey });
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // L-9 fix: 校验加载的环境值
                    var normalized = value!.ToUpperInvariant();
                    if (ValidEnvironments.Contains(normalized))
                        Current = normalized;
                    else
                        AppLogger.Warn($"AppConfig 中发现无效环境值 '{value}'，使用默认值 {DefaultEnvironment}。", typeof(EnvironmentManager));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"无法从数据库加载环境配置 ({ex.Message})，使用默认值 {DefaultEnvironment}。", typeof(EnvironmentManager));
            }
        }

        /// <summary>
        /// 切换环境（持久化到 AppConfig 表 + 更新内存状态）。
        /// 返回 true 表示切换成功。
        /// </summary>
        public bool SwitchTo(string environment, string connectionString)
        {
            var normalized = environment.ToUpperInvariant();
            if (!ValidEnvironments.Contains(normalized))
                throw new ArgumentException($"无效的环境名: {environment}。有效值: DEV, PROD");

            try
            {
                using var conn = new SQLiteConnection(connectionString);
                conn.Open();

                // UPSERT
                conn.Execute(
                    @"INSERT INTO AppConfig (Key, Value, Description, UpdatedAt)
                      VALUES (@Key, @Value, '运行环境', datetime('now'))
                      ON CONFLICT(Key) DO UPDATE SET Value=@Value, UpdatedAt=datetime('now')",
                    new { Key = ConfigKey, Value = normalized });

                Current = normalized;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"SwitchTo failed: {ex.Message}", ex, typeof(EnvironmentManager));
                return false;
            }
        }
    }
}
