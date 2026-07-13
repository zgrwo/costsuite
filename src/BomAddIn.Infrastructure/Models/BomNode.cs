using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Models
{
    /// <summary>BOM 结构节点 — 邻接表模型（Instance 面）</summary>
    public class BomNode
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public long ParentMaterialId { get; set; }
        public long ChildMaterialId { get; set; }
        public double Quantity { get; set; }
        public string Position { get; set; } = string.Empty;   // 位号/参考标识符
        /// <summary>损耗率 (0.0 ~ 1.0)</summary>
        public double ScrapRate { get; set; }
        /// <summary>BOM 视图类型: EBOM / MBOM / CBOM</summary>
        public string BomViewType { get; set; } = "EBOM";
        public int Level { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public VersionState VersionState { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
