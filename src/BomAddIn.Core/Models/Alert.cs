using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Models
{
    /// <summary>预警信息</summary>
    public class Alert
    {
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TriggeredRule { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? NodeCode { get; set; }
        public string? Dimension { get; set; }

        public override string ToString()
        {
            return $"[{Severity}] {Message} (Rule: {TriggeredRule})";
        }
    }
}
