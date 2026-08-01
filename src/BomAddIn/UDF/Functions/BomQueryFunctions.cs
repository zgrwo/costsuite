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
        // V1.0 默认 OrgId=1，多组织场景需改为可配置 (与 DataQueryFunctions.DefaultOrgId 对齐)
        private const long DefaultOrgId = 1;

        /// <summary>
        /// =BOMEXPAND(itemCode, [asOfDate], [versionState])
        /// 展开指定物料的完整 BOM 结构，返回多层级扁平列表。
        /// </summary>
        // spec §8.1: 纯查询函数标记 IsThreadSafe = true，允许 Excel 并行计算。
        // 线程安全保证: Container.BeginScope() 每次创建独立 DI scope;
        // BomAnalysisProvider 内部 lock 保护 DuckDB 连接; MemoryCacheProvider 线程安全。
        [ExcelFunction(Name = "BOMEXPAND", Description = "展开指定物料的完整BOM结构",
            IsThreadSafe = true, IsVolatile = false)]
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

                // F-01 fix: 版本检查前移 — 非 Released 参数在 V1.0 不可达，
                // 提前返回避免触发昂贵的 DuckDB BOM 展开后再丢弃结果。
                // V1.1: BomAnalysisProvider.ExpandBom 接受 versionState 参数后移除此限制。
                if (version != "Released")
                    return ExcelError.ExcelErrorValue;

                using var scope = Container.BeginScope();
                var service = scope.ServiceProvider.GetRequiredService<IBomService>();
                var nodes = service.Expand(itemCode, date);

                if (nodes.Count == 0)
                    return ExcelError.ExcelErrorNA;

                // 若 BOM 深度超限，BomAnalysisProvider 会在结果末尾追加 [TRUNCATED] 哨兵节点（Level=-1），
                // 该节点作为输出中可见的警告行，用户可据此判断结果可能不完整。

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
            IsThreadSafe = true, IsVolatile = false)]
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

                // U-2 fix: 直接调用 CalculateCost（内部已包含 Expand + 缓存）。
                // 仅当成本为 0 时用轻量主键查询区分“物料不存在”与“成本确实为 0”。
                var cost = service.CalculateCost(itemCode, date);

                if (cost == 0)
                {
                    var materialRepo = scope.ServiceProvider.GetRequiredService<BomAddIn.Data.Repositories.IMaterialRepository>();
                    if (materialRepo.GetByCode(DefaultOrgId, itemCode) == null)
                        return ExcelError.ExcelErrorNA;
                }

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
