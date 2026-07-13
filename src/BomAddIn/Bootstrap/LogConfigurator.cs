using System;
using System.IO;
using BomAddIn.Bootstrap;
using NLog;
using NLog.Config;
using NLog.Filters;
using NLog.Targets;

namespace BomAddIn
{
    /// <summary>NLog 日志初始化 — 文件 + 调试输出双通道</summary>
    internal static class LogConfigurator
    {
        /// <summary>NLog 初始化完成标志，供 Infrastructure 层日志门面判断。</summary>
        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BomAddIn", "Logs");
                Directory.CreateDirectory(logDir);

                var config = new LoggingConfiguration();

                // R2-12: 文件目标 — 分级采样（Info 1% + Warn/Error/Fatal 全量）
                var fileTarget = new FileTarget("file")
                {
                    FileName = Path.Combine(logDir, "${shortdate}.log"),
                    Layout = "${longdate} | ${level:uppercase=true} | ${logger} | ${message} ${exception:format=tostring}",
                    ArchiveEvery = FileArchivePeriod.Day,
                    MaxArchiveFiles = 7,
                    Encoding = System.Text.Encoding.UTF8
                };
                config.AddTarget(fileTarget);

                // 调试输出目标（仅在 DEBUG 构建中）
                var debugTarget = new DebuggerTarget("debug")
                {
                    Layout = "[BomAddIn] ${level} ${logger}: ${message}"
                };
                config.AddTarget(debugTarget);

                // R2-12: Info 级别采样写入文件（限制日志量），Warn+ 全量写入
                config.AddRule(LogLevel.Warn, LogLevel.Fatal, fileTarget);
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, debugTarget);

                // 采样规则: Info 级别仅 1% 写入文件（plan.md §6.3 "NLog 分级采样"）
                var sampledFileRule = new NLog.Config.LoggingRule("*", LogLevel.Info, LogLevel.Info, fileTarget)
                {
                    FilterDefaultAction = NLog.Filters.FilterResult.Neutral
                };
                sampledFileRule.Filters.Add(new ConditionBasedFilter
                {
                    Condition = "ticks mod 100 != 0",
                    Action = FilterResult.IgnoreFinal
                });
                config.LoggingRules.Add(sampledFileRule);

                LogManager.Configuration = config;

                // R2-11: 注入 NLog 桥接器到 AppLogger，使所有层统一走 NLog
                BomAddIn.Infrastructure.Logging.AppLogger.LogSinkFactory = name => new NLogAppSink(name);

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine($"[LogConfigurator] NLog 初始化失败: {ex.Message}");
            }
        }
    }
}
