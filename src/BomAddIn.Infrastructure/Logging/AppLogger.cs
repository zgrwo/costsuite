using System;
using System.Runtime.CompilerServices;

namespace BomAddIn.Infrastructure.Logging
{
    /// <summary>
    /// 轻量应用日志门面 — 跨层统一日志入口。
    /// 当 BomAddIn 启动并完成日志初始化后，自动桥接到文件日志；
    /// 在此之前（及日志不可用时）fallback 到 Debug.WriteLine。
    ///
    /// 使用方式:
    ///   AppLogger.Info("BOM 展开完成", typeof(BomService));
    ///   AppLogger.Error("同步失败", ex, typeof(SyncService));
    /// </summary>
    public static class AppLogger
    {
        /// <summary>日志槽工厂委托。由 BomAddIn 在日志初始化后注入。</summary>
        public static Func<string, IAppLogSink>? LogSinkFactory { get; set; }

        private static IAppLogSink GetSink(Type? callerType = null)
        {
            var loggerName = callerType?.FullName ?? "BomAddIn";
            if (LogSinkFactory != null)
            {
                try { return LogSinkFactory(loggerName); }
                catch { /* fall through to fallback */ }
            }
            return new DebugFallbackSink(loggerName);
        }

        public static void Debug(string message, Type? callerType = null)
            => GetSink(callerType).Log("DEBUG", message, null);

        public static void Info(string message, Type? callerType = null)
            => GetSink(callerType).Log("INFO", message, null);

        public static void Warn(string message, Type? callerType = null)
            => GetSink(callerType).Log("WARN", message, null);

        public static void Error(string message, Exception? ex = null, Type? callerType = null)
            => GetSink(callerType).Log("ERROR", message, ex);

        public static void Fatal(string message, Exception? ex = null, Type? callerType = null)
            => GetSink(callerType).Log("FATAL", message, ex);
    }

    /// <summary>日志槽抽象 — 允许 BomAddIn 注入文件日志实现，其他层不依赖具体日志库。</summary>
    public interface IAppLogSink
    {
        void Log(string level, string message, Exception? ex);
    }

    /// <summary>Debug.WriteLine fallback — 在文件日志不可用时使用。</summary>
    internal sealed class DebugFallbackSink : IAppLogSink
    {
        private readonly string _name;
        public DebugFallbackSink(string name) => _name = name;

        public void Log(string level, string message, Exception? ex)
        {
            var text = $"[{_name}] {level}: {message}";
            if (ex != null) text += $"\n{ex}";
            System.Diagnostics.Debug.WriteLine(text);
        }
    }
}
