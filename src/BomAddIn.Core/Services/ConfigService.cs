using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Repositories;

namespace BomAddIn.Core.Services
{
    /// <summary>应用配置服务 — MemoryCache 前置 + SQLite 持久化</summary>
    /// <remarks>
    /// 读取路径: 请求 → MemoryCache (L1) → SQLite → 回填缓存
    /// 写入路径: 写入 SQLite → 刷新 MemoryCache
    /// 缓存 TTL: 10 分钟（SetValue 后立即过期刷新）
    /// </remarks>
    public class ConfigService : IConfigService
    {
        private readonly IAppConfigRepository _repository;
        private readonly ICacheProvider _cache;
        private readonly IAuthorizationService _authz;

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
        private const string CacheKeyPrefix = "config:";

        /// <summary>M-9: 最后一次 TryGetValue 转换失败的错误信息</summary>
        public string? LastConversionError { get; private set; }

        public ConfigService(IAppConfigRepository repository, ICacheProvider cache,
            IAuthorizationService authz)
        {
            _repository = repository;
            _cache = cache;
            _authz = authz;
        }

        public string GetValue(string key, string defaultValue = "")
        {
            var cacheKey = CacheKeyPrefix + key;

            var cached = _cache.Get<string>(cacheKey);
            if (cached != null)
                return cached;

            var record = _repository.GetByKey(key);
            if (record == null)
                return defaultValue;

            _cache.Set(cacheKey, record.Value, CacheTtl);
            return record.Value;
        }

        public bool TryGetValue<T>(string key, out T? value)
        {
            value = default;
            LastConversionError = null;

            // M-9: 先检查缓存中是否存在该 key，区分"键不存在"和"转换失败"
            if (!_cache.Exists(CacheKeyPrefix + key) && _repository.GetByKey(key) == null)
            {
                LastConversionError = $"配置键 '{key}' 不存在。";
                return false;
            }

            var raw = GetValue(key);
            if (string.IsNullOrEmpty(raw))
            {
                LastConversionError = $"配置键 '{key}' 的值为空。";
                return false;
            }

            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                var converted = converter.ConvertFromString(raw);
                if (converted != null)
                {
                    value = (T)converted;
                    return true;
                }
            }
            catch (NotSupportedException ex)
            {
                LastConversionError = $"类型转换器不支持: 键 '{key}' → {typeof(T).Name}: {ex.Message}";
            }
            catch (FormatException ex)
            {
                LastConversionError = $"格式错误: 键 '{key}' 的值 '{raw}' 无法转换为 {typeof(T).Name}: {ex.Message}";
            }
            catch (Exception ex)
            {
                LastConversionError = $"转换失败: 键 '{key}' → {typeof(T).Name}: {ex.Message}";
            }
            return false;
        }

        public void SetValue(string key, string value, string? description = null, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.ConfigUpdate);
            var config = new AppConfig
            {
                Key = key,
                Value = value,
                Description = description ?? string.Empty,
                UpdatedAt = DateTime.UtcNow
            };
            _repository.Set(config);

            // 立即刷新缓存
            var cacheKey = CacheKeyPrefix + key;
            _cache.Set(cacheKey, value, CacheTtl);
        }

        public IEnumerable<KeyValuePair<string, string>> GetAll()
        {
            return _repository.GetAll()
                .Select(c => new KeyValuePair<string, string>(c.Key, c.Value));
        }

        public void WarmUp()
        {
            var all = _repository.GetAll();
            foreach (var config in all)
            {
                var cacheKey = CacheKeyPrefix + config.Key;
                _cache.Set(cacheKey, config.Value, CacheTtl);
            }
        }
    }
}
