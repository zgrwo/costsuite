using System;

namespace BomAddIn.Data.Caching
{
    public interface ICacheProvider
    {
        T? Get<T>(string key) where T : class;
        void Set<T>(string key, T value, TimeSpan? ttl = null);
        void Remove(string key);
        void RemoveByPrefix(string prefix);
        bool Exists(string key);
    }
}
