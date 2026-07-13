using System;

namespace BomAddIn.Infrastructure.Models
{
    public class AuditLog
    {
        public long Id { get; set; }
        public long? UserId { get; set; }
        public string Action { get; set; } = string.Empty;  // CREATE/UPDATE/DELETE
        public string TableName { get; set; } = string.Empty;
        public long? RecordId { get; set; }
        public string? OldValues { get; set; }   // JSON
        public string? NewValues { get; set; }   // JSON
        public DateTime Timestamp { get; set; }
    }
}
