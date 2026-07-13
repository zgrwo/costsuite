using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Logging;

namespace BomAddIn.Core.Services
{
    /// <summary>差异计算引擎 — LINQ set-diff + 数值比较</summary>
    /// <remarks>纯函数，线程安全。V1.0 内存计算，百万级数据时考虑 DuckDB join。</remarks>
    public class VarianceCalculator : IVarianceCalculator
    {
        public List<VarianceResult> CompareBomVersions(
            List<BomExpandedNode> versionA,
            List<BomExpandedNode> versionB)
        {
            var results = new List<VarianceResult>();

            // 使用 (ItemCode, ParentMaterialId, Level) 复合键处理同一物料在多层级出现的情况
            // 例: 螺丝 MAT-000042 在 Level=2 和 Level=3 各出现一次，是两个独立的 BOM 节点
            // C-3 fix: 同键多节点（同物料同层级多次出现）追加序号避免 ToDictionary 崩溃
            string NodeKey(BomExpandedNode n) => $"{n.ItemCode}|{n.ParentMaterialId}|{n.Level}";

            var mapA = ToDictionarySafe(versionA, NodeKey);
            var mapB = ToDictionarySafe(versionB, NodeKey);

            var allKeys = new HashSet<string>(mapA.Keys);
            allKeys.UnionWith(mapB.Keys);

            foreach (var key in allKeys)
            {
                var inA = mapA.TryGetValue(key, out var nodeA);
                var inB = mapB.TryGetValue(key, out var nodeB);

                if (inA && !inB)
                {
                    results.Add(new VarianceResult
                    {
                        NodeCode = nodeA!.ItemCode,
                        NodeDescription = $"{nodeA.Description} (L{nodeA.Level})",
                        ChangeType = VarianceChangeType.Removed,
                        Dimension = VarianceDimension.BomStructure,
                        OldValue = nodeA.Quantity.ToString("F3"),
                        NewValue = null
                    });
                }
                else if (!inA && inB)
                {
                    results.Add(new VarianceResult
                    {
                        NodeCode = nodeB!.ItemCode,
                        NodeDescription = $"{nodeB.Description} (L{nodeB.Level})",
                        ChangeType = VarianceChangeType.Added,
                        Dimension = VarianceDimension.BomStructure,
                        OldValue = null,
                        NewValue = nodeB.Quantity.ToString("F3")
                    });
                }
                else if (Math.Abs(nodeA!.Quantity - nodeB!.Quantity) > 0.0001 * Math.Max(1.0, Math.Max(nodeA.Quantity, nodeB.Quantity))
                         || nodeA.Level != nodeB.Level)
                {
                    var oldQty = nodeA.Quantity;
                    var newQty = nodeB.Quantity;
                    var levelChanged = nodeA.Level != nodeB.Level;
                    var qtyChanged = Math.Abs(nodeA.Quantity - nodeB.Quantity) > 0.0001 * Math.Max(1.0, Math.Max(nodeA.Quantity, nodeB.Quantity));
                    var changePct = oldQty > 0 ? (newQty - oldQty) / oldQty * 100 : (newQty != 0 ? double.PositiveInfinity : 0);

                    // M-11: 区分层级变化 vs 数量变化的消息
                    string description;
                    oldQty = nodeA.Quantity;
                    newQty = nodeB.Quantity;
                    if (levelChanged && !qtyChanged)
                        description = $"{nodeA.Description} (层级 L{nodeA.Level}→L{nodeB.Level})";
                    else if (levelChanged && qtyChanged)
                        description = $"{nodeA.Description} (L{nodeA.Level}→L{nodeB.Level}, 数量变化 {changePct:F1}%)";
                    else
                        description = $"{nodeA.Description} (L{nodeA.Level})";

                    results.Add(new VarianceResult
                    {
                        NodeCode = nodeA.ItemCode,
                        NodeDescription = description,
                        ChangeType = VarianceChangeType.Modified,
                        Dimension = VarianceDimension.BomStructure,
                        OldValue = oldQty.ToString("F3"),
                        NewValue = newQty.ToString("F3"),
                        ChangePercent = double.IsInfinity(changePct) ? 100.0 : (double)Math.Round(changePct, 2)
                    });
                }
            }

            return results.OrderByDescending(r => r.ChangeType).ToList();
        }

        /// <summary>
        /// 安全的 ToDictionary——同键多节点时追加序号后缀 (#1, #2, ...) 避免崩溃。
        /// </summary>
        private static Dictionary<string, BomExpandedNode> ToDictionarySafe(
            List<BomExpandedNode> nodes, Func<BomExpandedNode, string> keySelector)
        {
            var dict = new Dictionary<string, BomExpandedNode>();
            foreach (var node in nodes)
            {
                var baseKey = keySelector(node);
                var key = baseKey;
                var suffix = 1;
                while (dict.ContainsKey(key))
                {
                    suffix++;
                    key = $"{baseKey}#{suffix}";
                }
                dict[key] = node;
            }
            return dict;
        }

        public List<VarianceResult> ComparePrices(
            long materialId,
            decimal priceA, DateTime dateA, string currencyA,
            decimal priceB, DateTime dateB, string currencyB)
        {
            var results = new List<VarianceResult>();

            // H-15: 币种不同时跳过比较并记录日志
            if (!string.Equals(currencyA, currencyB, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn($"物料 MAT-{materialId} 币种不同 ({currencyA} vs {currencyB})，跳过价格比较。", typeof(VarianceCalculator));
                results.Add(new VarianceResult
                {
                    NodeCode = $"MAT-{materialId}",
                    NodeDescription = $"价格比较跳过 — 币种不同 ({currencyA} vs {currencyB})",
                    ChangeType = VarianceChangeType.Unchanged,
                    Dimension = VarianceDimension.Price,
                    OldValue = $"{priceA:F4} {currencyA}",
                    NewValue = $"{priceB:F4} {currencyB}",
                    ChangePercent = null
                });
                return results;
            }

            // H-15: 使用相对阈值替代绝对阈值
            // 当差异 < priceA * 0.01%（最小 0.001m）时视为无变化
            var relativeThreshold = Math.Max(0.001m, priceA * 0.0001m);
            if (Math.Abs(priceA - priceB) < relativeThreshold)
                return results;

            var changePct = priceA > 0 ? (priceB - priceA) / priceA * 100 : 0;

            results.Add(new VarianceResult
            {
                NodeCode = $"MAT-{materialId}",
                NodeDescription = $"价格差异: {dateA:yyyy-MM-dd} → {dateB:yyyy-MM-dd}",
                ChangeType = VarianceChangeType.Modified,
                Dimension = VarianceDimension.Price,
                OldValue = priceA.ToString("F4"),
                NewValue = priceB.ToString("F4"),
                ChangePercent = (double)Math.Round(changePct, 2)
            });

            return results;
        }

        /// <summary>比较库存差异 (V1.2 🔜 — 当前返回未实现标记)</summary>
        public List<VarianceResult> CompareInventory(
            long materialId,
            double quantityA, DateTime dateA, string warehouseA,
            double quantityB, DateTime dateB, string warehouseB)
        {
            return new List<VarianceResult>
            {
                new VarianceResult
                {
                    NodeCode = $"MAT-{materialId}",
                    NodeDescription = $"库存差异比较 (V1.2 待实现): {warehouseA} @{dateA:yyyy-MM-dd} vs @{dateB:yyyy-MM-dd}",
                    ChangeType = VarianceChangeType.Unchanged,
                    Dimension = VarianceDimension.Price, // 占位，V1.2 应新增 VarianceDimension.Inventory
                    OldValue = quantityA.ToString("F2"),
                    NewValue = quantityB.ToString("F2")
                }
            };
        }

        /// <summary>比较预算差异 (V1.2 🔜 — 当前返回未实现标记)</summary>
        public List<VarianceResult> CompareBudget(
            long estimateId,
            decimal estimatedCost, decimal actualCost,
            double estimatedHours, double actualHours)
        {
            return new List<VarianceResult>
            {
                new VarianceResult
                {
                    NodeCode = $"EST-{estimateId}",
                    NodeDescription = $"预算差异比较 (V1.2 待实现): 预估 {estimatedCost:C} vs 实际 {actualCost:C}",
                    ChangeType = VarianceChangeType.Unchanged,
                    Dimension = VarianceDimension.Price, // 占位，V1.2 应新增 VarianceDimension.Budget
                    OldValue = estimatedCost.ToString("F2"),
                    NewValue = actualCost.ToString("F2")
                }
            };
        }
    }
}
