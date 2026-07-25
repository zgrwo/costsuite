using System;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UDF
{
    /// <summary>
    /// 全局服务定位器 — 仅在 UDF 中使用（UDF 无法进行构造函数注入）。
    /// UI 代码应使用标准构造函数注入。
    ///
    /// Scoped 服务: 每次 Resolve 创建新 Scope（模拟 per-call）。
    /// Singleton 服务: 直接返回。
    ///
    /// 自 code-review-2026-07-13 起: Resolve&lt;T&gt;() 已弃用，
    /// 改用 ResolveWithScope&lt;T&gt;() 避免 scope 在返回前被释放。
    /// </summary>
    public static class Container
    {
        private static volatile IServiceProvider? _provider;

        public static void Initialize(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 解析单个服务并返回其 scope。适用于仅需一个服务的 UDF。
        /// 调用方负责 using 释放 scope。
        /// </summary>
        public static (T Service, IDisposable Scope) ResolveWithScope<T>() where T : class
        {
            if (_provider == null)
                throw new InvalidOperationException("Container 未初始化。请确保 AutoOpen 已调用 Container.Initialize()。");

            var scope = _provider.CreateScope();
            return (scope.ServiceProvider.GetRequiredService<T>(), scope);
        }

        /// <summary>
        /// 创建新 scope，供需要多个服务的 UDF 使用。
        /// 调用方负责 using 释放 scope，通过 scope.ServiceProvider 手动解析。
        /// </summary>
        public static IServiceScope BeginScope()
        {
            if (_provider == null)
                throw new InvalidOperationException("Container 未初始化。请确保 AutoOpen 已调用 Container.Initialize()。");

            return _provider.CreateScope();
        }

        [Obsolete("使用 ResolveWithScope&lt;T&gt;() 或 BeginScope() 代替。原方法在返回前释放 scope，导致服务不可用。")]
        public static T Resolve<T>() where T : class
        {
            throw new NotSupportedException("Use ResolveWithScope<T>() or BeginScope() instead.");
        }
    }
}
