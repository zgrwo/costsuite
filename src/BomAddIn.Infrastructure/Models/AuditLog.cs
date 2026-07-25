using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Models
{
    public class AuditLog
    {
        public long Id { get; set; }
        public long? UserId { get; set; }
        public AuditAction Action { get; set; }
        public string TableName { get; set; } = string.Empty;
        public long? RecordId { get; set; }
        public string? OldValues { get; set; }   // JSON
        public string? NewValues { get; set; }   // JSON
        public DateTime Timestamp { get; set; }
    }
}
