using System.Collections.Generic;
using BomAddIn.Core.Models;

namespace BomAddIn.Core.Services
{
    /// <summary>预警规则评估器接口</summary>
    public interface IAlertEvaluator
    {
        /// <summary>对差异结果列表运行规则引擎，返回触发的预警</summary>
        List<Alert> Evaluate(IEnumerable<VarianceResult> variances);
    }
}
