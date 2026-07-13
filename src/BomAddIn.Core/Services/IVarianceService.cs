using System;
using System.Collections.Generic;
using BomAddIn.Core.Models;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Core.Services
{
    /// <summary>差异分析编排服务接口</summary>
    public interface IVarianceService
    {
        /// <summary>全维度差异分析：结构 + 价格 + 预警</summary>
        VarianceAnalysisResult RunFullAnalysis(
            List<BomExpandedNode> bomVersionA, DateTime dateA,
            List<BomExpandedNode> bomVersionB, DateTime dateB);
    }

    /// <summary>全维度分析结果</summary>
    public class VarianceAnalysisResult
    {
        public List<VarianceResult> StructureVariances { get; set; } = new();
        public List<VarianceResult> PriceVariances { get; set; } = new();
        public List<Alert> Alerts { get; set; } = new();
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    }
}
