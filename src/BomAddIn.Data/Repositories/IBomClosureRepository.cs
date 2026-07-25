using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    /// <summary>BOM Closure Table 查询接口 — O(1) 子树/祖先查询</summary>
    public interface IBomClosureRepository
    {
        /// <summary>获取指定物料的所有后代（BOM 展开）</summary>
        /// <param name="materialId">祖先物料 ID</param>
        /// <returns>所有后代节点（含深度和路径数量），按 Depth 排序</returns>
        IEnumerable<BomClosureNode> GetDescendants(long materialId);

        /// <summary>获取指定物料的所有祖先（Where-Used）</summary>
        /// <param name="materialId">后代物料 ID</param>
        /// <returns>所有祖先节点（含深度），按 Depth 排序</returns>
        IEnumerable<BomClosureNode> GetAncestors(long materialId);

        /// <summary>获取直接子节点（depth=1）</summary>
        IEnumerable<BomClosureNode> GetDirectChildren(long materialId);

        /// <summary>获取子树最大深度</summary>
        int GetMaxDepth(long materialId);

        /// <summary>全量重建 Closure Table（同步/批量导入后调用）</summary>
        void Rebuild();

        /// <summary>Closure Table 是否有数据</summary>
        bool HasData();
    }

    /// <summary>Closure Table 查询结果</summary>
    public class BomClosureNode
    {
        /// <summary>相关物料 ID（查询后代时为 DescendantId，查询祖先时为 AncestorId）</summary>
        public long MaterialId { get; set; }

        /// <summary>层级深度（1=直接子/父，2=孙/祖...）</summary>
        public int Depth { get; set; }

        /// <summary>累积路径数量（从根到该节点的数量乘积）</summary>
        public double PathQuantity { get; set; }
    }
}
