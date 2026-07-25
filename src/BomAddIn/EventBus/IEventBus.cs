using System;

namespace BomAddIn.EventBus
{
    /// <summary>
    /// 进程内轻量事件总线接口 (code-review C-3: 注册到 DI 体系)。
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void Publish<T>(T @event);
        void Clear();
    }
}
