using System;
using System.Collections.Generic;
using BomAddIn.Core.Models;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Core.Services
{
    /// <summary>差异计算引擎接口 — 纯计算，无状态</summary>
    /// <remarks>
    /// V1.1 已实现: 结构差异 (BomStructure) + 价格差异 (Price)
    /// V1.2 计划: 库存差异 (Inventory) + 预算差异 (Budget) + 供应商差异 (Supplier) + 时间切片差异 (TimeSlice)
    /// 参见 spec §6.1 全部 6 个差异维度定义。
    /// </remarks>
    public interface IVarianceCalculator
    {
        /// <summary>比较两个 BOM 版本的结构差异 (V1.1 ✅)</summary>
        List<VarianceResult> CompareBomVersions(
            List<BomExpandedNode> versionA,
            List<BomExpandedNode> versionB);

        /// <summary>比较两个时间点的价格差异 (V1.1 ✅)</summary>
        List<VarianceResult> ComparePrices(
            long materialId,
            decimal priceA, DateTime dateA, string currencyA,
            decimal priceB, DateTime dateB, string currencyB);

        /// <summary>比较库存差异 (V1.2 🔜)</summary>
        List<VarianceResult> CompareInventory(
            long materialId,
            double quantityA, DateTime dateA, string warehouseA,
            double quantityB, DateTime dateB, string warehouseB);

        /// <summary>比较预算差异 (V1.2 🔜)</summary>
        List<VarianceResult> CompareBudget(
            long estimateId,
            decimal estimatedCost, decimal actualCost,
            double estimatedHours, double actualHours);
    }
}
