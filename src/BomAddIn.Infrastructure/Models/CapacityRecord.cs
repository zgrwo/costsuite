using System;

namespace BomAddIn.Infrastructure.Models
{
    /// <summary>ERP 同步的产能记录（只读缓存）</summary>
    public class CapacityRecord
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public string WorkCenterId { get; set; } = string.Empty;
        public double CapacityHours { get; set; }
        public long DataVersion { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
