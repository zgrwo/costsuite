using System;

namespace BomAddIn.Core.Events
{
    public class AlertTriggeredEvent
    {
        /// <summary>预警触发时间</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>预警类型</summary>
        public string AlertType { get; set; } = string.Empty;
        /// <summary>严重级别</summary>
        public string Severity { get; set; } = string.Empty;
        /// <summary>预警消息</summary>
        public string Message { get; set; } = string.Empty;
        /// <summary>相关物料 ID（如有）</summary>
        public long? MaterialId { get; set; }
    }
}
