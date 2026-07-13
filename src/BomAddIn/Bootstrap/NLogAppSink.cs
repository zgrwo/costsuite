using System;
using BomAddIn.Infrastructure.Logging;
using NLog;

namespace BomAddIn.Bootstrap
{
    /// <summary>NLog → AppLogger 桥接器。在 LogConfigurator.Initialize() 之后注入。</summary>
    internal sealed class NLogAppSink : IAppLogSink
    {
        private readonly Logger _logger;

        public NLogAppSink(string name)
        {
            _logger = LogManager.GetLogger(name);
        }

        public void Log(string level, string message, Exception? ex)
        {
            var logLevel = level switch
            {
                "DEBUG" => NLog.LogLevel.Debug,
                "INFO" => NLog.LogLevel.Info,
                "WARN" => NLog.LogLevel.Warn,
                "ERROR" => NLog.LogLevel.Error,
                "FATAL" => NLog.LogLevel.Fatal,
                _ => NLog.LogLevel.Info
            };

            var logEvent = new LogEventInfo(logLevel, _logger.Name, message);
            if (ex != null) logEvent.Exception = ex;
            _logger.Log(logEvent);
        }
    }
}
