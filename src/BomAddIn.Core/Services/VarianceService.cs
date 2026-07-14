using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Core.Services
{
    /// <summary>差异分析编排 — 组合 Calculator + Evaluator + PriceRepo</summary>
    public class VarianceService : IVarianceService
    {
        private readonly IVarianceCalculator _calculator;
        private readonly IAlertEvaluator _evaluator;
        private readonly IPriceRecordRepository _priceRepo;

        public VarianceService(
            IVarianceCalculator calculator,
            IAlertEvaluator evaluator,
            IPriceRecordRepository priceRepo)
        {
            _calculator = calculator;
            _evaluator = evaluator;
            _priceRepo = priceRepo;
        }

        public VarianceAnalysisResult RunFullAnalysis(
            List<BomExpandedNode> bomVersionA, DateTime dateA,
            List<BomExpandedNode> bomVersionB, DateTime dateB)
        {
            if (bomVersionA == null) throw new ArgumentNullException(nameof(bomVersionA));
            if (bomVersionB == null) throw new ArgumentNullException(nameof(bomVersionB));

            var result = new VarianceAnalysisResult
            {
                AnalysisTime = DateTime.UtcNow
            };

            // 1. 结构差异
            var structureVariances = _calculator.CompareBomVersions(bomVersionA, bomVersionB);
            result.StructureVariances = structureVariances;

            // 2. 价格差异 — 基于物料展开结果中的 MaterialId 按版本时间拉取单价
            var priceVariances = CalculatePriceVariances(bomVersionA, bomVersionB, dateA, dateB);
            result.PriceVariances = priceVariances;

            // 3. 综合预警评估
            var allVariances = structureVariances
                .Concat(priceVariances)
                .ToList();
            result.Alerts = _evaluator.Evaluate(allVariances);

            return result;
        }

        private List<VarianceResult> CalculatePriceVariances(
            List<BomExpandedNode> versionA,
            List<BomExpandedNode> versionB,
            DateTime dateA,
            DateTime dateB)
        {
            var results = new List<VarianceResult>();

            // 汇总两个版本的唯一物料 ID 集合
            var materialIds = new HashSet<long>();
            foreach (var n in versionA) materialIds.Add(n.MaterialId);
            foreach (var n in versionB) materialIds.Add(n.MaterialId);

            // C-2 fix: 批量查询替代 N+1 循环 — 2 次 SQL 而非 2N 次
            var pricesA = _priceRepo.GetByMaterialIdsAndDate(materialIds, dateA);
            var pricesB = _priceRepo.GetByMaterialIdsAndDate(materialIds, dateB);

            foreach (var materialId in materialIds)
            {
                pricesA.TryGetValue(materialId, out var priceRecordA);
                pricesB.TryGetValue(materialId, out var priceRecordB);

                if (priceRecordA != null && priceRecordB != null)
                {
                    var priceResults = _calculator.ComparePrices(
                        materialId,
                        priceRecordA.UnitPrice, priceRecordA.EffectiveDate, priceRecordA.Currency,
                        priceRecordB.UnitPrice, priceRecordB.EffectiveDate, priceRecordB.Currency);
                    results.AddRange(priceResults);
                }
                // M-2: 只有单侧价格数据时，生成提示性差异记录
                else if (priceRecordA != null && priceRecordB == null)
                {
                    results.Add(new VarianceResult
                    {
                        NodeCode = $"MAT-{materialId}",
                        NodeDescription = $"价格数据缺失: 版本B ({dateB:yyyy-MM-dd}) 无价格记录",
                        ChangeType = VarianceChangeType.Unchanged,
                        Dimension = VarianceDimension.Price,
                        OldValue = priceRecordA.UnitPrice.ToString("F4"),
                        NewValue = null,
                        ChangePercent = null
                    });
                }
                else if (priceRecordA == null && priceRecordB != null)
                {
                    results.Add(new VarianceResult
                    {
                        NodeCode = $"MAT-{materialId}",
                        NodeDescription = $"价格数据缺失: 版本A ({dateA:yyyy-MM-dd}) 无价格记录",
                        ChangeType = VarianceChangeType.Unchanged,
                        Dimension = VarianceDimension.Price,
                        OldValue = null,
                        NewValue = priceRecordB.UnitPrice.ToString("F4"),
                        ChangePercent = null
                    });
                }
            }

            return results;
        }
    }
}
