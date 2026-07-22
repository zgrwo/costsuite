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
            // Filter out truncation sentinel nodes (Level == -1) — they are metadata, not BOM data
            versionA = versionA.Where(n => n.Level >= 0).ToList();
            versionB = versionB.Where(n => n.Level >= 0).ToList();

            var results = new List<VarianceResult>();

            // 使用 (ItemCode, ParentMaterialId, Level) 复合键处理同一物料在多层级出现的情况
            string NodeKey(BomExpandedNode n) => $"{n.ItemCode}|{n.ParentMaterialId}|{n.Level}";

            // C-3 fix: 分组比较替代全局字典，解决跨版本键匹配误差
            // 全局 ToDictionary 在 A 有 2 个同键节点 / B 有 1 个时，A 的 #2 后缀节点在 B 中无匹配→假 Removed
            // 分组比较：每个 (ItemCode, ParentMaterialId, Level) 组内按确定性排序后逐位比较
            // G-2 fix: 排序以 Description 为主键（比 Quantity 更稳定），MaterialId 为最终平局裁决
            var groupsA = versionA.GroupBy(NodeKey).ToDictionary(
                g => g.Key, g => g.OrderBy(n => n.MaterialId).ThenBy(n => n.Description).ThenBy(n => n.Quantity).ToList());
            var groupsB = versionB.GroupBy(NodeKey).ToDictionary(
                g => g.Key, g => g.OrderBy(n => n.MaterialId).ThenBy(n => n.Description).ThenBy(n => n.Quantity).ToList());

            var allKeys = new HashSet<string>(groupsA.Keys);
            allKeys.UnionWith(groupsB.Keys);

            foreach (var key in allKeys)
            {
                var listA = groupsA.TryGetValue(key, out var la) ? la : new List<BomExpandedNode>();
                var listB = groupsB.TryGetValue(key, out var lb) ? lb : new List<BomExpandedNode>();
                var maxCount = Math.Max(listA.Count, listB.Count);

                for (int i = 0; i < maxCount; i++)
                {
                    if (i >= listA.Count)
                    {
                        // A 中无此位置的节点 → Added
                        var nb = listB[i];
                        results.Add(new VarianceResult
                        {
                            NodeCode = nb.ItemCode,
                            NodeDescription = $"{nb.Description} (L{nb.Level})",
                            ChangeType = VarianceChangeType.Added,
                            Dimension = VarianceDimension.BomStructure,
                            OldValue = null,
                            NewValue = nb.Quantity.ToString("F3")
                        });
                    }
                    else if (i >= listB.Count)
                    {
                        // B 中无此位置的节点 → Removed
                        var na = listA[i];
                        results.Add(new VarianceResult
                        {
                            NodeCode = na.ItemCode,
                            NodeDescription = $"{na.Description} (L{na.Level})",
                            ChangeType = VarianceChangeType.Removed,
                            Dimension = VarianceDimension.BomStructure,
                            OldValue = na.Quantity.ToString("F3"),
                            NewValue = null
                        });
                    }
                    else
                    {
                        var nodeA = listA[i];
                        var nodeB = listB[i];

                        if (Math.Abs(nodeA.Quantity - nodeB.Quantity) > 0.0001 * Math.Max(1.0, Math.Max(nodeA.Quantity, nodeB.Quantity))
                            || nodeA.Level != nodeB.Level)
                        {
                            var oldQty = nodeA.Quantity;
                            var newQty = nodeB.Quantity;
                            var levelChanged = nodeA.Level != nodeB.Level;
                            var qtyChanged = Math.Abs(nodeA.Quantity - nodeB.Quantity) > 0.0001 * Math.Max(1.0, Math.Max(nodeA.Quantity, nodeB.Quantity));
                            var changePct = oldQty > 0 ? (newQty - oldQty) / oldQty * 100 : (newQty != 0 ? double.PositiveInfinity : 0);

                            string description;
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
                                ChangePercent = double.IsInfinity(changePct) ? null : (double)Math.Round(changePct, 2)
                            });
                        }
                    }
                }
            }

            return results.OrderByDescending(r => r.ChangeType).ToList();
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
                    ChangeType = VarianceChangeType.Unchanged, // 币种不同时标记为Unchanged，message中注明跳过原因
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

            // H-29: priceA==0 且 priceB!=0 时使用哨兵值 MaxValue，
            // AlertEvaluator 识别哨兵值并生成"价格从零变为正值"告警
            double? changePct;
            if (priceA > 0)
                changePct = (double)Math.Round((priceB - priceA) / priceA * 100, 2);
            else
                changePct = priceB != 0 ? double.MaxValue : 0;

            results.Add(new VarianceResult
            {
                NodeCode = $"MAT-{materialId}",
                NodeDescription = $"价格差异: {dateA:yyyy-MM-dd} → {dateB:yyyy-MM-dd}",
                ChangeType = VarianceChangeType.Modified,
                Dimension = VarianceDimension.Price,
                OldValue = priceA.ToString("F4"),
                NewValue = priceB.ToString("F4"),
                ChangePercent = changePct
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
                    Dimension = VarianceDimension.Inventory,
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
                    Dimension = VarianceDimension.Budget,
                    OldValue = estimatedCost.ToString("F2"),
                    NewValue = actualCost.ToString("F2")
                }
            };
        }
    }
}
