using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Models;
using Moq;

namespace BomAddIn.PerformanceTests;

/// <summary>
/// BOM 差异计算性能基准 (spec §12.1 KPI: 1000节点 &lt;500ms, 10万节点 &lt;3s)。
/// 运行: dotnet run -c Release --project tests/BomAddIn.PerformanceTests
/// DuckDB BOM 展开基准需 native 库，当前仅覆盖 VarianceCalculator 内存计算路径。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class BomVarianceBenchmarks
{
    private VarianceCalculator _calculator = null!;
    private List<BomExpandedNode> _bom100 = null!;
    private List<BomExpandedNode> _bom1000 = null!;
    private List<BomExpandedNode> _bom10000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _calculator = new VarianceCalculator();
        _bom100 = BuildBom(100);
        _bom1000 = BuildBom(1000);
        _bom10000 = BuildBom(10000);
    }

    [Benchmark]
    [Arguments(100)]
    public void CompareBom_100() => _calculator.CompareBomVersions(_bom100, Clone(_bom100));

    [Benchmark]
    [Arguments(1000)]
    public void CompareBom_1000() => _calculator.CompareBomVersions(_bom1000, Clone(_bom1000));

    [Benchmark]
    [Arguments(10000)]
    public void CompareBom_10000() => _calculator.CompareBomVersions(_bom10000, Clone(_bom10000));

    private static List<BomExpandedNode> BuildBom(int count)
    {
        var nodes = new List<BomExpandedNode>(count);
        for (int i = 0; i < count; i++)
        {
            nodes.Add(new BomExpandedNode
            {
                ItemCode = $"MAT-{i:D6}",
                Description = $"Component {i}",
                Quantity = i * 1.5 + 0.1,
                Level = i % 5,
                MaterialId = i + 1,
                Unit = "pcs",
                ParentMaterialId = i > 0 ? (i - 1) : (long?)null
            });
        }
        return nodes;
    }

    private static List<BomExpandedNode> Clone(List<BomExpandedNode> source)
    {
        return source.Select(n => new BomExpandedNode
        {
            ItemCode = n.ItemCode, Description = n.Description,
            Quantity = n.Quantity, Level = n.Level,
            MaterialId = n.MaterialId, Unit = n.Unit,
            ParentMaterialId = n.ParentMaterialId
        }).ToList();
    }
}

/// <summary>
/// 轻量 AlertEvaluator 性能基准。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class AlertEvaluatorBenchmarks
{
    private AlertEvaluator _evaluator = null!;
    private List<VarianceResult> _variances = null!;

    [Params(100, 1000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _evaluator = new AlertEvaluator(Mock.Of<IConfigProvider>());
        _variances = new List<VarianceResult>(Count);
        for (int i = 0; i < Count; i++)
        {
            _variances.Add(new VarianceResult
            {
                NodeCode = $"MAT-{i:D6}",
                ChangeType = i % 3 == 0 ? VarianceChangeType.Added
                           : i % 3 == 1 ? VarianceChangeType.Modified
                           : VarianceChangeType.Removed,
                Dimension = i % 2 == 0 ? VarianceDimension.BomStructure : VarianceDimension.Price,
                ChangePercent = i * 0.75
            });
        }
    }

    [Benchmark]
    public List<Alert> Evaluate() => _evaluator.Evaluate(_variances);
}
