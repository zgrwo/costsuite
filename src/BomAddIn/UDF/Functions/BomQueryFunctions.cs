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

                // 版本过滤: Expand 默认返回 Released；All 需服务端支持（V1.1 待实现）
                if (version == "Draft")
                    nodes = nodes.Where(n => n.VersionState == "Draft").ToList();
                else if (version == "Obsolete")
                    nodes = nodes.Where(n => n.VersionState == "Obsolete").ToList();
                // "Released" 和 "All" 直接使用 Expand 结果（默认已过滤 Released）

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
                AppLogger.Warn($"BOMEXPAND 错误: {ex.Message}", typeof(BomQueryFunctions));
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
                    return ExcelError.ExcelErrorNA;

                return cost;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"BOMCOST 错误: {ex.Message}", typeof(BomQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
