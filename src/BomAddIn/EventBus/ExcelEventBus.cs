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
    /// handler 使用 WeakReference 包裹，防止订阅者忘记退订导致内存泄漏。
    /// 每次 Publish 时自动清理已回收的订阅。
    /// </summary>
    public class ExcelEventBus : IEventBus
    {
        private readonly Dictionary<Type, List<WeakReference<Delegate>>> _handlers = new();
        private readonly object _lock = new();

        /// <summary>订阅事件</summary>
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
        /// 取消订阅。注意：必须传入与 Subscribe 相同的委托实例。
        /// Lambda 表达式每次创建新实例，因此对 Lambda 的 Unsubscribe 无效。
        /// 建议将委托保存为字段/变量以便后续取消。
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
            }
        }

        /// <summary>发布事件（同步，在发布线程执行所有 handler）</summary>
        public void Publish<T>(T @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            List<(int Index, Delegate Target)> handlers;
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list)) return;

                // 快照 + 清理死引用：遍历所有 weak ref，只保留存活的
                var snapshot = new List<Delegate>(list.Count);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].TryGetTarget(out var target))
                        snapshot.Add(target);
                    else
                        list.RemoveAt(i); // 清理已回收的订阅
                }

                if (list.Count == 0)
                {
                    _handlers.Remove(typeof(T));
                    return;
                }

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
