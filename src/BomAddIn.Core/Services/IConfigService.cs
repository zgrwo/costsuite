using System.Collections.Generic;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>应用配置服务 — 带缓存的键值配置读写</summary>
    public interface IConfigService
    {
        /// <summary>最后一次 TryGetValue 转换失败的错误信息（M-9）</summary>
        string? LastConversionError { get; }

        /// <summary>获取配置值，不存在时返回 defaultValue</summary>
        string GetValue(string key, string defaultValue = "");

        /// <summary>泛型解析获取（支持 int、bool、string 等）</summary>
        bool TryGetValue<T>(string key, out T? value);

        /// <summary>设置配置值（upsert），自动刷新缓存</summary>
        void SetValue(string key, string value, string? description = null, UserRole callerRole = UserRole.Admin);

        /// <summary>获取全部配置项</summary>
        IEnumerable<KeyValuePair<string, string>> GetAll();

        /// <summary>预热 — 启动时加载全部配置到缓存</summary>
        void WarmUp();
    }
}
