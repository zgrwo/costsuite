using System;

namespace BomAddIn.Infrastructure.Models
{
    public class SyncLog
    {
        public long Id { get; set; }
        public string SyncType { get; set; } = string.Empty;  // Full/Incremental/Materials/Prices
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int RecordsProcessed { get; set; }
        public string Status { get; set; } = "Pending";  // Pending/Running/Complete/Error
        public string? ErrorMessage { get; set; }
    }
}
