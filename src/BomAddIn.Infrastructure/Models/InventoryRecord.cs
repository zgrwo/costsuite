using System;

namespace BomAddIn.Infrastructure.Models
{
    /// <summary>ERP 同步的库存记录（只读缓存）</summary>
    public class InventoryRecord
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public long MaterialId { get; set; }
        public string WarehouseId { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public long DataVersion { get; set; }
        public DateTime SnapshotDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
