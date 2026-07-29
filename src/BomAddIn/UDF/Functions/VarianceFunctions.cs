using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.UDF.Helpers;
using ExcelDna.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UDF.Functions
{
    public static class VarianceFunctions
    {
        /// <summary>
        /// =VARIANCECHECK(itemCodeA, dateA, itemCodeB, dateB)
        /// 比较两个 BOM 版本的结构差异。
        /// </summary>
        [ExcelFunction(Name = "VARIANCECHECK", Description = "比较两个BOM版本的差异",
            IsThreadSafe = true, IsVolatile = false)]
        public static object VarianceCheck(
            [ExcelArgument("物料编码 A")] string itemCodeA,
            [ExcelArgument("版本 A 日期")] object? asOfDateA = null,
            [ExcelArgument("物料编码 B")] string? itemCodeB = null,
            [ExcelArgument("版本 B 日期")] object? asOfDateB = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCodeA))
                    return ExcelError.ExcelErrorNA;

                // 如果只提供一个物料编码，比较同一物料两个时间点
                var codeB = string.IsNullOrWhiteSpace(itemCodeB) ? itemCodeA : itemCodeB;
                // H-28: 未提供日期时，dateA 默认 3 个月前，dateB 默认今天，确保能看到差异
                var dateA = UdfParameterParser.ParseDateArg(asOfDateA) ?? DateTime.Today.AddMonths(-3);
                var dateB = UdfParameterParser.ParseDateArg(asOfDateB) ?? DateTime.Today;

                using var scope = Container.BeginScope();
                var sp = scope.ServiceProvider;
                var bomService = sp.GetRequiredService<IBomService>();
                var versionA = bomService.Expand(itemCodeA, dateA);
                var versionB = bomService.Expand(codeB!, dateB);

                var varianceService = sp.GetRequiredService<IVarianceService>();
                var result = varianceService.RunFullAnalysis(versionA, dateA, versionB, dateB);

                var allVariances = result.StructureVariances
                    .Concat(result.PriceVariances)
                    .ToList();

                if (allVariances.Count == 0)
                    return ExcelError.ExcelErrorNA;

                var headers = new[] { "NodeCode", "ChangeType", "Dimension", "OldValue", "NewValue" };
                return UdfParameterParser.ToRectangularArray(allVariances, v => new object[]
                {
                    v.NodeCode,
                    v.ChangeType.ToString(),
                    v.Dimension.ToString(),
                    v.OldValue ?? "",
                    v.NewValue ?? ""
                }, headers);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UDF 错误: {ex.Message}", typeof(VarianceFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }

        /// <summary>
        /// =ALERTCHECK(itemCode)
        /// 检查物料 BOM 中所有节点的价格异常和结构预警。
        /// </summary>
        [ExcelFunction(Name = "ALERTCHECK", Description = "检查物料BOM预警状态（价格变化、数量异常）",
            IsThreadSafe = true, IsVolatile = false)]
        public static object AlertCheck(
            [ExcelArgument("物料编码（可选）")] object? itemCode = null)
        {
            try
            {
                var code = itemCode as string;
                using var scope = Container.BeginScope();
                var sp = scope.ServiceProvider;
                var evaluator = sp.GetRequiredService<IAlertEvaluator>();
                var calculator = sp.GetRequiredService<IVarianceCalculator>();
                var priceRepo = sp.GetRequiredService<BomAddIn.Data.Repositories.IPriceRecordRepository>();

                var allVariances = new List<VarianceResult>();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    // 展开指定物料 BOM
                    var bomService = sp.GetRequiredService<IBomService>();
                    var nodes = bomService.Expand(code!, DateTime.Today);

                    if (nodes.Count > 0)
                    {
                        // U-2 fix: 批量获取所有物料的价格历史，消除 N+1 查询
                        var materialIds = nodes.Select(n => n.MaterialId).Distinct().ToList();
                        var from = DateTime.Today.AddMonths(-3);
                        var to = DateTime.Today;

                        // 批量查询所有物料的价格（一次 SQL）
                        var allPriceHistory = priceRepo.GetHistoryBatch(materialIds, from, to);
                        var historyByMaterial = allPriceHistory
                            .GroupBy(p => p.MaterialId)
                            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.EffectiveDate).ToList());

                        foreach (var node in nodes)
                        {
                            if (historyByMaterial.TryGetValue(node.MaterialId, out var prices) && prices.Count >= 2)
                            {
                                var latest = prices[prices.Count - 1];
                                var previous = prices[prices.Count - 2];
                                var priceVariances = calculator.ComparePrices(
                                    node.MaterialId,
                                    previous.UnitPrice, previous.EffectiveDate, "CNY",
                                    latest.UnitPrice, latest.EffectiveDate, "CNY");
                                allVariances.AddRange(priceVariances);
                            }
                        }
                    }
                }

                var alerts = evaluator.Evaluate(allVariances);

                if (alerts.Count == 0)
                    return ExcelError.ExcelErrorNA;

                var headers = new[] { "Severity", "Message", "Rule", "NodeCode" };
                return UdfParameterParser.ToRectangularArray(alerts, a => new object[]
                {
                    a.Severity.ToString(),
                    a.Message,
                    a.TriggeredRule,
                    a.NodeCode ?? ""
                }, headers);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UDF 错误: {ex.Message}", typeof(VarianceFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
