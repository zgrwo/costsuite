namespace BomAddIn.Infrastructure.Config
{
    public interface IConfigProvider
    {
        string Get(string key);
        T? Get<T>(string key);
        void Set(string key, string value);

        /// <summary>尝试获取配置值。键存在时返回 true。</summary>
        bool TryGet(string key, out string value);

        /// <summary>检查配置键是否存在。</summary>
        bool Contains(string key);
    }
}
