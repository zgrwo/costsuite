using System;

namespace BomAddIn.Infrastructure.Models
{
    /// <summary>ERP 同步的订单记录（只读缓存）</summary>
    public class OrderRecord
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public long MaterialId { get; set; }
        public double OrderQty { get; set; }
        public DateTime DueDate { get; set; }
        public long DataVersion { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
