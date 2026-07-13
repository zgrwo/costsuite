using System;

namespace BomAddIn.Infrastructure.Models
{
    /// <summary>ERP 同步的价格记录（只读缓存）</summary>
    /// <remarks>
    /// 精度说明：UnitPrice 使用 C# decimal（28-29 位有效数字），但数据库列是 REAL（IEEE 754 double，约 15-17 位有效数字）。
    /// 往返转换 decimal → REAL → decimal 时可能损失精度（如 0.1 在 double 中无法精确表示）。
    /// 财务精度敏感场景（如发票对账）应在应用层以 decimal 计算，以数据库值为展示参考（±1e-12 级别差异）。
    /// 如需高精度存储，可将数据库列改为 TEXT 存储 decimal.ToString("R") 或使用 INTEGER 以分为单位。
    /// </remarks>
    public class PriceRecord
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public long MaterialId { get; set; }
        public long SupplierId { get; set; }

        /// <summary>单价（decimal → SQLite REAL：注意精度损失，详见类注释）</summary>
        public decimal UnitPrice { get; set; }
        public string Currency { get; set; } = "CNY";
        public long DataVersion { get; set; }
        public DateTime EffectiveDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
