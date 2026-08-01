using System;
using System.Collections.Generic;
using System.Linq;

namespace BomAddIn.EventBus
{
    /// <summary>
    /// 轻量事件总线 — 进程内 pub-sub。
    /// V1.0 简单实现: 基于 Dictionary + List 的同步发布。
    /// UI 订阅者自行通过 Dispatcher 封送回 UI 线程。
    ///
    /// 默认使用 WeakReference 包裹 handler，防止订阅者忘记退订导致内存泄漏。
    /// ❗ 注意: Lambda 订阅者必须将委托保存为字段，否则 GC 后订阅静默失效。
    /// 对于必须保持活跃的关键订阅（如 Dashboard），使用 SubscribeStrong。
    /// </summary>
    public class ExcelEventBus : IEventBus
    {
        private readonly Dictionary<Type, List<WeakReference<Delegate>>> _handlers = new();
        private readonly Dictionary<Type, List<Delegate>> _strongHandlers = new();
        private readonly object _lock = new();

        /// <summary>订阅事件（弱引用 — 订阅者必须保持委托存活，否则 GC 后静默失效）</summary>
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                {
                    list = new List<WeakReference<Delegate>>();
                    _handlers[typeof(T)] = list;
                }
                list.Add(new WeakReference<Delegate>(handler));
            }
        }

        /// <summary>
        /// 订阅事件（强引用 — 适用于必须保持活跃的关键订阅，如 Dashboard 刷新）。
        /// 订阅者必须在适当时机调用 Unsubscribe 释放，否则会导致内存泄漏。
        /// </summary>
        public void SubscribeStrong<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (!_strongHandlers.TryGetValue(typeof(T), out var list))
                {
                    list = new List<Delegate>();
                    _strongHandlers[typeof(T)] = list;
                }
                list.Add(handler);
            }
        }

        /// <summary>
        /// 取消订阅。注意：必须传入与 Subscribe 相同的委托实例。
        /// Lambda 表达式每次创建新实例，因此对 Lambda 的 Unsubscribe 无效。
        /// 建议将委托保存为字段/变量以便后续取消。
        /// 同时检查弱引用和强引用订阅字典。
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    // 移除匹配的 handler（存活的和已回收的无效引用一并清理）
                    list.RemoveAll(wr =>
                    {
                        if (wr.TryGetTarget(out var existing))
                            return existing == (Delegate)handler;
                        return true; // 已回收的引用也移除
                    });
                    if (list.Count == 0)
                        _handlers.Remove(typeof(T));
                }

                // F-03 fix: 同时从强引用字典移除，确保 SubscribeStrong 的 handler 可通过 Unsubscribe 释放
                if (_strongHandlers.TryGetValue(typeof(T), out var strongList))
                {
                    strongList.RemoveAll(d => d == (Delegate)handler);
                    if (strongList.Count == 0)
                        _strongHandlers.Remove(typeof(T));
                }
            }
        }

        /// <summary>发布事件（同步，在发布线程执行所有 handler）</summary>
        public void Publish<T>(T @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            List<(int Index, Delegate Target)> handlers;
            lock (_lock)
            {
                // 弱引用 handlers
                var snapshot = new List<Delegate>();
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i].TryGetTarget(out var target))
                            snapshot.Add(target);
                        else
                            list.RemoveAt(i);
                    }
                    if (list.Count == 0)
                        _handlers.Remove(typeof(T));
                }

                // AR-2 fix: 强引用 handlers 始终参与发布
                if (_strongHandlers.TryGetValue(typeof(T), out var strongList))
                    snapshot.AddRange(strongList);

                if (snapshot.Count == 0) return;
                handlers = snapshot.Select(d => (0, d)).ToList();
            }

            foreach (var (_, handler) in handlers)
            {
                try
                {
                    ((Action<T>)handler)(@event);
                }
                catch (Exception ex)
                {
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
                _strongHandlers.Clear();
            }
        }
    }
}
