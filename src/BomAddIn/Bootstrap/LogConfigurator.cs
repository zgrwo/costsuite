using System;
using System.IO;
using BomAddIn.Infrastructure.Logging;

namespace BomAddIn
{
    /// <summary>日志初始化 — 文件 + 调试输出双通道（无第三方依赖）</summary>
    internal static class LogConfigurator
    {
        /// <summary>日志初始化完成标志。</summary>
        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BomAddIn", "Logs");
                Directory.CreateDirectory(logDir);

                // 注入文件日志槽到 AppLogger，使所有层统一写入日志文件
                AppLogger.LogSinkFactory = name => new FileLogSink(name, logDir);

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine($"[LogConfigurator] 日志初始化失败: {ex.Message}");
            }
        }
    }

    /// <summary>轻量文件日志槽 — 按日分文件，Warn+ 写入文件，全量输出到 Debug。</summary>
    internal sealed class FileLogSink : IAppLogSink
    {
        private readonly string _name;
        private readonly string _logDir;
        private static readonly object WriteLock = new object();

        public FileLogSink(string name, string logDir)
        {
            _name = name;
            _logDir = logDir;
        }

        public void Log(string level, string message, Exception? ex)
        {
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {level} | {_name} | {message}";
            if (ex != null) text += $"\n{ex}";

            // Debug 输出（全量）
            System.Diagnostics.Debug.WriteLine($"[BomAddIn] {level} {_name}: {message}");

            // 文件输出（Warn+ 全量，Info 采样 1%）
            bool writeToFile = level == "WARN" || level == "ERROR" || level == "FATAL"
                            || (level == "INFO" && DateTime.Now.Ticks % 100 == 0);
            if (!writeToFile) return;

            try
            {
                var file = Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                lock (WriteLock)
                {
                    File.AppendAllText(file, text + Environment.NewLine);
                }
            }
            catch { /* 日志写入失败不应影响业务 */ }
        }
    }
}
