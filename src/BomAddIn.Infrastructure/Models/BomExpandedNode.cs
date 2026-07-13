namespace BomAddIn.Infrastructure.Models
{
    /// <summary>BOM 展开后的扁平化节点 — 从 DuckDB CTE 结果映射</summary>
    public class BomExpandedNode
    {
        public int Level { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        /// <summary>BOM 视图类型 (EBOM/MBOM/CBOM)，用作 Source (Make/Buy) 的代理</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>版本状态，用于 UDF 层面的版本过滤</summary>
        public string VersionState { get; set; } = "Released";
        public long MaterialId { get; set; }
        public long? ParentMaterialId { get; set; }
    }
}
