namespace BomAddIn.Core.Models
{
    /// <summary>差异类型</summary>
    public enum VarianceChangeType
    {
        Added,      // 新增节点
        Removed,    // 删除节点
        Modified,   // 数量/价格变化
        Unchanged   // 无变化
    }

    /// <summary>差异维度</summary>
    public enum VarianceDimension
    {
        BomStructure,   // BOM 结构差异
        Price,          // 价格差异
        Inventory,      // 库存差异
        Budget          // 预算差异
    }

    /// <summary>差异比对结果</summary>
    public class VarianceResult
    {
        public string NodeCode { get; set; } = string.Empty;
        public string NodeDescription { get; set; } = string.Empty;
        public VarianceChangeType ChangeType { get; set; }
        public VarianceDimension Dimension { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public double? ChangePercent { get; set; }

        public override string ToString()
        {
            return $"[{Dimension}] {NodeCode}: {ChangeType} ({OldValue} → {NewValue})";
        }
    }
}
