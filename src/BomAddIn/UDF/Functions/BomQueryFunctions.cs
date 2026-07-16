using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Infrastructure.Models;
using BomAddIn.UDF.Helpers;
using ExcelDna.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UDF.Functions
{
    public static class BomQueryFunctions
    {
        /// <summary>
        /// =BOMEXPAND(itemCode, [asOfDate], [versionState])
        /// 展开指定物料的完整 BOM 结构，返回多层级扁平列表。
        /// </summary>
        [ExcelFunction(Name = "BOMEXPAND", Description = "展开指定物料的完整BOM结构",
            IsThreadSafe = false, IsVolatile = false)]
        public static object BomExpand(
            [ExcelArgument("物料编码")] string itemCode,
            [ExcelArgument("截止日期（默认今天）")] object? asOfDate = null,
            [ExcelArgument("版本状态: Draft/Released/All（默认Released）")] object? versionState = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                    return ExcelError.ExcelErrorNA;

                var date = UdfParameterParser.ParseDateArg(asOfDate) ?? DateTime.Today;
                var version = UdfParameterParser.ParseVersionState(versionState as string);

                using var scope = Container.BeginScope();
                var service = scope.ServiceProvider.GetRequiredService<IBomService>();
                var nodes = service.Expand(itemCode, date);

                // V1.0 限制: DuckDB ExpandBom 硬编码 VersionState='Released'，
                // "Draft"/"Obsolete"/"All" 参数均不可达（Expand 仅返回 Released 节点）。
                // V1.1: BomAnalysisProvider.ExpandBom 接受 versionState 参数进行动态过滤。
                // 当前 "Released" 和 "All" 直接使用 Expand 结果。
                if (version == "Draft" || version == "Obsolete")
                    return ExcelError.ExcelErrorNA; // 功能未实现，明确返回 #N/A 而非静默空列表

                if (nodes.Count == 0)
                    return ExcelError.ExcelErrorNA;

                // B-4 fix: 添加 Source (Make/Buy) 列，匹配规范 §5.4 的 6 列输出
                var headers = new[] { "Level", "ItemCode", "Description", "Quantity", "Unit", "Source" };
                return UdfParameterParser.ToRectangularArray(nodes, n => new object[]
                {
                    n.Level,
                    n.ItemCode,
                    n.Description,
                    Math.Round(n.Quantity, 6),
                    n.Unit,
                    n.Source
                }, headers);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"BOMEXPAND 错误: {ex}", ex, typeof(BomQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }

        /// <summary>
        /// =BOMCOST(itemCode, [asOfDate])
        /// 计算物料完整 BOM 汇总成本 — 自底向上汇总。
        /// 叶节点 = Quantity × UnitPrice; 中间节点 = 自身成本 + 所有子节点成本之和。
        /// </summary>
        [ExcelFunction(Name = "BOMCOST", Description = "计算物料汇总成本（自底向上汇总 Quantity×UnitPrice）",
            IsThreadSafe = false, IsVolatile = false)]
        public static object BomCost(
            [ExcelArgument("物料编码")] string itemCode,
            [ExcelArgument("截止日期（默认今天）")] object? asOfDate = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                    return ExcelError.ExcelErrorNA;

                var date = UdfParameterParser.ParseDateArg(asOfDate) ?? DateTime.Today;
                using var scope = Container.BeginScope();
                var service = scope.ServiceProvider.GetRequiredService<IBomService>();
                var cost = service.CalculateCost(itemCode, date);

                if (cost == 0)
                    return 0.0;  // 返回零成本（正常结果），调用方可用 BOMEXPAND 确认物料存在性

                return cost;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"BOMCOST 错误: {ex}", ex, typeof(BomQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
