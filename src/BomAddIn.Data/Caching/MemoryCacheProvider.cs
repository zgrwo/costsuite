using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BomAddIn.Data.Caching
{
    /// <summary>L1 进程内 MemoryCache — 简单实现，Sprint 5 升级为 Microsoft.Extensions.Caching.Memory</summary>
    public class MemoryCacheProvider : ICacheProvider
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        public T? Get<T>(string key) where T : class
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                // R2-08: 先捕获值和时间戳，避免 TTL 检查与 TryRemove 之间的 TOCTOU 窗口
                var expiresAt = entry.ExpiresAt;
                var value = entry.Value;
                if (expiresAt > DateTime.UtcNow)
                    return value as T;
                // 按 key 删除，但仅当当前值仍为同一条过期 entry（防止误删刷新后的新值）
                _cache.TryRemove(key, out var existing);
            }
            return null;
        }

        public void Set<T>(string key, T value, TimeSpan? ttl = null)
        {
            var expiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(5));
            _cache[key] = new CacheEntry { Value = value, ExpiresAt = expiresAt };
        }

        public void Remove(string key) => _cache.TryRemove(key, out _);

        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            foreach (var key in _cache.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    _cache.TryRemove(key, out _);
            }
        }

        public bool Exists(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (_cache.TryGetValue(key, out var entry))
            {
                // R2-08: 检查 TTL，过期后删除（极小 TOCTOU 窗口：误删将导致下次访问重建，不产生数据错误）
                if (entry.ExpiresAt > DateTime.UtcNow)
                    return true;
                _cache.TryRemove(key, out _);
            }
            return false;
        }

        private class CacheEntry
        {
            public object? Value { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
