using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>现有测试的边界值和错误路径补充</summary>
public class VarianceCalculatorEdgeCases
{
    private readonly VarianceCalculator _calc = new();

    [Fact]
    public void CompareBomVersions_NullInput_ShouldThrow()
    {
        // LINQ 在遇到 null 集合时抛出 ArgumentNullException
        Action act = () => _calc.CompareBomVersions(null!, new List<BomExpandedNode>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CompareBomVersions_TinyDifference_BelowThreshold_NotModified()
    {
        // 阈值在 VarianceCalculator 中是 > 0.0001
        var a = new List<BomExpandedNode> { new() { ItemCode = "X", Quantity = 1.0, Level = 1, MaterialId = 1 } };
        var b = new List<BomExpandedNode> { new() { ItemCode = "X", Quantity = 1.00005, Level = 1, MaterialId = 1 } };

        // 差异 = 0.00005 < 0.0001 → 应视为未变化
        var results = _calc.CompareBomVersions(a, b);
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComparePrices_ExactSame_ReturnsEmpty()
    {
        var results = _calc.ComparePrices(1, 100.000m, DateTime.Today, "CNY", 100.000m, DateTime.Today, "CNY");
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComparePrices_NegativeChange_Works()
    {
        var results = _calc.ComparePrices(1, 100, DateTime.Today, "CNY", 50, DateTime.Today, "CNY");
        results.Should().HaveCount(1);
        results[0].ChangePercent.Should().Be(-50);
    }

    [Fact]
    public void CompareBomVersions_LargeDataset_CompletesWithoutError()
    {
        var count = 5000;
        var a = new List<BomExpandedNode>(count);
        var b = new List<BomExpandedNode>(count);
        for (int i = 0; i < count; i++)
        {
            a.Add(new BomExpandedNode { ItemCode = $"MAT-{i:D6}", Quantity = i, Level = i % 5, MaterialId = i + 1 });
            b.Add(new BomExpandedNode { ItemCode = $"MAT-{i:D6}", Quantity = i % 3 == 0 ? i * 2 : i, Level = i % 5, MaterialId = i + 1 });
        }

        var results = _calc.CompareBomVersions(a, b);
        results.Should().NotBeNull();
    }

    [Fact]
    public void CompareBomVersions_EmptyFirst_AllItemsReturnedAsAdded()
    {
        var b = new List<BomExpandedNode> { new() { ItemCode = "A", Quantity = 1, Level = 1, MaterialId = 1 } };
        var results = _calc.CompareBomVersions(new(), b);
        results.Should().AllSatisfy(r => r.ChangeType.Should().Be(VarianceChangeType.Added));
    }
}

public class AlertEvaluatorEdgeCases
{
    private readonly AlertEvaluator _evaluator = new(Mock.Of<IConfigProvider>());

    [Fact]
    public void Evaluate_NullInput_ShouldThrow()
    {
        Action act = () => _evaluator.Evaluate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ChangePercentAtBoundary_51Percent_TriggersRule()
    {
        var v = new VarianceResult
        {
            ChangeType = VarianceChangeType.Modified,
            Dimension = VarianceDimension.BomStructure,
            ChangePercent = 51.0
        };
        var alerts = _evaluator.Evaluate(new[] { v });
        alerts.Should().Contain(a => a.TriggeredRule == "BOM_QTY_LARGE_CHANGE");
    }

    [Fact]
    public void Evaluate_ChangePercentAtBoundary_50Percent_DoesNotTrigger()
    {
        var v = new VarianceResult
        {
            ChangeType = VarianceChangeType.Modified,
            Dimension = VarianceDimension.BomStructure,
            ChangePercent = 50.0
        };
        var alerts = _evaluator.Evaluate(new[] { v });
        alerts.Should().NotContain(a => a.TriggeredRule == "BOM_QTY_LARGE_CHANGE");
    }

    [Fact]
    public void Evaluate_PriceChangeAtBoundary_11Percent_TriggersWarning()
    {
        var v = new VarianceResult
        {
            Dimension = VarianceDimension.Price,
            ChangePercent = 11.0,
            ChangeType = VarianceChangeType.Modified
        };
        var alerts = _evaluator.Evaluate(new[] { v });
        alerts.Should().Contain(a => a.TriggeredRule == "PRICE_CHANGE_WARNING");
    }

    [Fact]
    public void Evaluate_PriceChangeAtBoundary_26Percent_TriggersError()
    {
        var v = new VarianceResult
        {
            Dimension = VarianceDimension.Price,
            ChangePercent = 26.0,
            ChangeType = VarianceChangeType.Modified
        };
        var alerts = _evaluator.Evaluate(new[] { v });
        alerts.Should().Contain(a => a.TriggeredRule == "PRICE_CHANGE_SEVERE");
    }
}
