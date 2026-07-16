using System;
using System.Collections.Generic;
using System.Globalization;
using BomAddIn.Core.Models;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>预警规则引擎 — 基于阈值的简单规则匹配</summary>
    public class AlertEvaluator : IAlertEvaluator
    {
        private const double DefaultPriceSevereThreshold = 25.0;
        private const double DefaultPriceWarningThreshold = 10.0;
        private const double DefaultPriceCriticalThreshold = 50.0;
        private const double DefaultBomQuantityChangeThreshold = 50.0;

        private readonly double _priceSevereThreshold;
        private readonly double _priceWarningThreshold;
        private readonly double _priceCriticalThreshold;
        private readonly double _bomQuantityChangeThreshold;

        public AlertEvaluator(IConfigProvider config)
        {
            _priceSevereThreshold = ParseConfig(config, "Alert:PriceSevereThreshold", DefaultPriceSevereThreshold);
            _priceWarningThreshold = ParseConfig(config, "Alert:PriceWarningThreshold", DefaultPriceWarningThreshold);
            _priceCriticalThreshold = ParseConfig(config, "Alert:PriceCriticalThreshold", DefaultPriceCriticalThreshold);
            _bomQuantityChangeThreshold = ParseConfig(config, "Alert:BomQuantityChangeThreshold", DefaultBomQuantityChangeThreshold);

            // C-13 fix: 验证阈值单调性，防止配置错误导致某些规则不可达
            // if-else 链中 Critical 优先匹配，必须满足 Critical > Severe > Warning
            if (!(_priceCriticalThreshold > _priceSevereThreshold
                  && _priceSevereThreshold > _priceWarningThreshold
                  && _priceWarningThreshold > 0))
            {
                throw new InvalidOperationException(
                    $"AlertEvaluator 阈值配置无效: Critical({_priceCriticalThreshold}) > Severe({_priceSevereThreshold}) > Warning({_priceWarningThreshold}) > 0 必须成立。" +
                    " 当前 if-else 链语义为 '仅报告最高严重级别'，阈值逆序将导致规则不可达。");
            }
        }

        public List<Alert> Evaluate(IEnumerable<VarianceResult> variances)
        {
            if (variances == null)
                throw new ArgumentNullException(nameof(variances));

            var alerts = new List<Alert>();

            foreach (var v in variances)
            {
                // 规则 1: BOM 节点被删除 → Warning
                if (v.Dimension == VarianceDimension.BomStructure
                    && v.ChangeType == VarianceChangeType.Removed)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Warning,
                        Message = $"BOM 节点被移除: {v.NodeCode} ({v.NodeDescription})",
                        TriggeredRule = "BOM_NODE_REMOVED",
                        NodeCode = v.NodeCode,
                        Dimension = "BomStructure"
                    });
                }

                // 规则 2: BOM 结构新增 → Info
                if (v.Dimension == VarianceDimension.BomStructure
                    && v.ChangeType == VarianceChangeType.Added)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Info,
                        Message = $"BOM 新增节点: {v.NodeCode} ({v.NodeDescription})",
                        TriggeredRule = "BOM_NODE_ADDED",
                        NodeCode = v.NodeCode,
                        Dimension = "BomStructure"
                    });
                }

                // 规则 3: 价格从零变为正值（哨兵值 double.MaxValue）→ Critical
                // double.MaxValue-1 == double.MaxValue（ULP ~1.8e292），显式检查 MaxValue 本身更清晰
                if (v.Dimension == VarianceDimension.Price
                    && v.ChangePercent.HasValue
                    && Math.Abs(v.ChangePercent.Value - double.MaxValue) < 1.0)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Critical,
                        Message = $"价格从零变为非零，变动无法计算百分比: {v.NodeCode} ({v.OldValue} → {v.NewValue})",
                        TriggeredRule = "PRICE_CHANGE_FROM_ZERO",
                        NodeCode = v.NodeCode,
                        Dimension = "Price"
                    });
                }
                // 规则 4: 价格波动 > criticalThreshold → Critical (M-4: 使 Critical 可达)
                else if (v.Dimension == VarianceDimension.Price
                    && v.ChangePercent.HasValue
                    && Math.Abs(v.ChangePercent.Value) > _priceCriticalThreshold)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Critical,
                        Message = $"价格波动 {v.ChangePercent:F1}%，超过 {_priceCriticalThreshold}% 临界阈值: {v.NodeCode} ({v.OldValue} → {v.NewValue})",
                        TriggeredRule = "PRICE_CHANGE_CRITICAL",
                        NodeCode = v.NodeCode,
                        Dimension = "Price"
                    });
                }
                // 规则 5: 价格波动 > severeThreshold → Error
                else if (v.Dimension == VarianceDimension.Price
                    && v.ChangePercent.HasValue
                    && Math.Abs(v.ChangePercent.Value) > _priceSevereThreshold)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Error,
                        Message = $"价格波动 {v.ChangePercent:F1}%，超过 {_priceSevereThreshold}% 严重阈值: {v.NodeCode} ({v.OldValue} → {v.NewValue})",
                        TriggeredRule = "PRICE_CHANGE_SEVERE",
                        NodeCode = v.NodeCode,
                        Dimension = "Price"
                    });
                }
                // 规则 6: 价格波动 > warningThreshold → Warning
                else if (v.Dimension == VarianceDimension.Price
                    && v.ChangePercent.HasValue
                    && Math.Abs(v.ChangePercent.Value) > _priceWarningThreshold)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Warning,
                        Message = $"价格波动 {v.ChangePercent:F1}%，超过 {_priceWarningThreshold}% 阈值: {v.NodeCode} ({v.OldValue} → {v.NewValue})",
                        TriggeredRule = "PRICE_CHANGE_WARNING",
                        NodeCode = v.NodeCode,
                        Dimension = "Price"
                    });
                }

                // 规则 7: BOM 数量变化 > 阈值 → Warning (H-16: 使用实际阈值)
                if (v.Dimension == VarianceDimension.BomStructure
                    && v.ChangeType == VarianceChangeType.Modified
                    && v.ChangePercent.HasValue
                    && Math.Abs(v.ChangePercent.Value) > _bomQuantityChangeThreshold)
                {
                    alerts.Add(new Alert
                    {
                        Severity = AlertSeverity.Warning,
                        Message = $"BOM 用量大幅变化 {v.ChangePercent:F1}%（超过 {_bomQuantityChangeThreshold}% 阈值）: {v.NodeCode} ({v.OldValue} → {v.NewValue})",
                        TriggeredRule = "BOM_QTY_LARGE_CHANGE",
                        NodeCode = v.NodeCode,
                        Dimension = "BomStructure"
                    });
                }
            }

            return alerts;
        }

        private static double ParseConfig(IConfigProvider config, string key, double defaultValue)
        {
            var raw = config.Get(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : defaultValue;
        }
    }
}
