using System;

namespace BomAddIn.EventBus
{
    /// <summary>
    /// 进程内轻量事件总线接口 (code-review C-3: 注册到 DI 体系)。
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler);
        /// <summary>强引用订阅 — 适用于必须保持活跃的关键订阅（如 Dashboard）。需手动 Unsubscribe。</summary>
        void SubscribeStrong<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void Publish<T>(T @event);
        void Clear();
    }
}
