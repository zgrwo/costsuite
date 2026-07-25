using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Models
{
    public class DataSnapshot
    {
        public long Id { get; set; }
        public SnapshotType SnapshotType { get; set; } = SnapshotType.Daily;
        public string SnapshotData { get; set; } = string.Empty;  // JSON/Binary
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
