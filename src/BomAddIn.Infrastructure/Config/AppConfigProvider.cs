using System;
using System.Collections.Concurrent;
using System.Data.SQLite;
using BomAddIn.Infrastructure.Logging;
using Dapper;

namespace BomAddIn.Infrastructure.Config
{
    /// <summary>
    /// 配置提供者 — 从 AppConfig 数据库表加载，硬编码默认值作为后备。
    /// 调用 LoadFromDb() 方法从数据库覆盖默认配置。
    /// </summary>
    public class AppConfigProvider : IConfigProvider
    {
        private ConcurrentDictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);

        public AppConfigProvider()
        {
            // 内置默认值 — 数据库配置优先覆盖。
            // ⚠️ 生产部署前必须通过 AppConfig 表或诊断工具配置实际 ERP 端点:
            //    INSERT INTO AppConfig (Key, Value) VALUES ('ErpApi:BaseUrl', 'https://your-erp.company.com/api');
            _config["ErpApi:HealthCheckUrl"] = "https://erp.example.com/api/health";
            _config["ErpApi:BaseUrl"] = "https://erp.example.com/api";
            _config["Sync:IntervalMinutes"] = "30";
            _config["Sync:RetryCount"] = "3";
        }

        /// <summary>
        /// 从 AppConfig 表中加载配置，覆盖内置默认值。
        /// 在 DI 注册后由 ServiceConfigurator 调用。
        /// </summary>
        public void LoadFromDb(string connectionString)
        {
            try
            {
                using var conn = new SQLiteConnection(connectionString);
                conn.Open();

                var rows = conn.Query<(string Key, string Value)>(
                    "SELECT Key, Value FROM AppConfig");

                var newConfig = new ConcurrentDictionary<string, string>(_config, StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in rows)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        newConfig[key] = value;
                }
                _config = newConfig;
            }
            catch (Exception ex)
            {
                // 数据库不可用时使用默认值
                AppLogger.Warn($"无法加载数据库配置 — {ex.Message}，使用默认值。", typeof(AppConfigProvider));
            }
        }

        public string Get(string key) =>
            _config.TryGetValue(key, out var value) ? value : string.Empty;

        public T? Get<T>(string key)
        {
            var value = Get(key);
            if (string.IsNullOrEmpty(value))
                return default;
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"无法转换配置键 '{key}' 的值 '{value}' 为类型 {typeof(T).Name} — {ex.Message}", typeof(AppConfigProvider));
                return default;
            }
        }

        public void Set(string key, string value) =>
            _config[key] = value;

        public bool TryGet(string key, out string value) =>
            _config.TryGetValue(key, out value);

        public bool Contains(string key) =>
            _config.ContainsKey(key);
    }
}
