using System;
using System.Collections.Generic;

namespace BomAddIn.EventBus
{
    /// <summary>
    /// 轻量事件总线 — 进程内 pub-sub。
    /// V1.0 简单实现: 基于 Dictionary + List 的同步发布。
    /// UI 订阅者自行通过 Dispatcher 封送回 UI 线程。
    /// </summary>
    public class ExcelEventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly object _lock = new();

        /// <summary>订阅事件</summary>
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(T)] = list;
                }
                list.Add(handler);
            }
        }

        /// <summary>取消订阅</summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    list.Remove(handler);
                    if (list.Count == 0)
                        _handlers.Remove(typeof(T));
                }
            }
        }

        /// <summary>发布事件（同步，在发布线程执行所有 handler）</summary>
        public void Publish<T>(T @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            List<Delegate> handlers;
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list)) return;
                handlers = new List<Delegate>(list); // 快照，避免迭代时修改
            }

            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<T>)handler)(@event);
                }
                catch (Exception ex)
                {
                    // 记录 handler 异常但不影响其他订阅者 (code-review C-4)
                    BomAddIn.Infrastructure.Logging.AppLogger.Error(
                        $"handler for {typeof(T).Name} threw", ex, typeof(ExcelEventBus));
                }
            }
        }

        /// <summary>清空所有订阅（AutoClose 时调用）</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }
    }
}
