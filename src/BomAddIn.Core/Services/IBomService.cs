using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    public interface IBomService
    {
        /// <summary>展开物料完整 BOM 树（DuckDB 递归 CTE）</summary>
        List<BomExpandedNode> Expand(string itemCode, DateTime? asOfDate = null);

        /// <summary>按 ID 查询 BomNode</summary>
        BomNode? GetById(long id);

        /// <summary>查询直接子节点</summary>
        IEnumerable<BomNode> GetChildren(long parentMaterialId, DateTime? asOfDate = null);

        /// <summary>新增 BOM 节点</summary>
        BomNode AddNode(BomNode node, long? userId = null, UserRole callerRole = UserRole.Admin);

        /// <summary>更新 BOM 节点</summary>
        void UpdateNode(BomNode node, long? userId = null, UserRole callerRole = UserRole.Admin);

        /// <summary>删除 BOM 节点</summary>
        void DeleteNode(long id, long? userId = null, UserRole callerRole = UserRole.Admin);

        /// <summary>获取 BOM 版本历史</summary>
        IEnumerable<BomVersion> GetVersionHistory(long bomId);

        /// <summary>计算物料完整 BOM 汇总成本（自底向上 Quantity×UnitPrice 汇总）</summary>
        double CalculateCost(string itemCode, DateTime? asOfDate = null);
    }
}
