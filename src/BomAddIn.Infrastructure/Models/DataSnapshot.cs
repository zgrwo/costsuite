using System;

namespace BomAddIn.Infrastructure.Models
{
    public class DataSnapshot
    {
        public long Id { get; set; }
        public string SnapshotType { get; set; } = "Daily";  // Daily/Manual
        public string SnapshotData { get; set; } = string.Empty;  // JSON/Binary
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
