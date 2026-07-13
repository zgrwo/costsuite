using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Config;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class AlertEvaluatorTests
{
    private readonly Mock<IConfigProvider> _configMock = new();
    private readonly AlertEvaluator _evaluator;

    public AlertEvaluatorTests()
    {
        _configMock.Setup(c => c.Get(It.IsAny<string>())).Returns(string.Empty);
        _evaluator = new AlertEvaluator(_configMock.Object);
    }

    [Fact]
    public void Evaluate_EmptyInput_ShouldReturnEmpty()
    {
        var alerts = _evaluator.Evaluate(Array.Empty<VarianceResult>());
        alerts.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_RemovedNode_ShouldTriggerRule()
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-001",
            NodeDescription = "Test Material",
            ChangeType = VarianceChangeType.Removed,
            Dimension = VarianceDimension.BomStructure
        };

        var alerts = _evaluator.Evaluate(new[] { variance });

        alerts.Should().HaveCount(1);
        alerts[0].TriggeredRule.Should().Be("BOM_NODE_REMOVED");
        alerts[0].Severity.Should().Be(AlertSeverity.Warning);
        alerts[0].NodeCode.Should().Be("MAT-001");
    }

    [Fact]
    public void Evaluate_AddedNode_ShouldTriggerRule()
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-002",
            NodeDescription = "New Material",
            ChangeType = VarianceChangeType.Added,
            Dimension = VarianceDimension.BomStructure
        };

        var alerts = _evaluator.Evaluate(new[] { variance });

        alerts.Should().HaveCount(1);
        alerts[0].TriggeredRule.Should().Be("BOM_NODE_ADDED");
        alerts[0].Severity.Should().Be(AlertSeverity.Info);
    }

    [Theory]
    [InlineData(30.0, "PRICE_CHANGE_SEVERE", AlertSeverity.Error)]
    [InlineData(15.0, "PRICE_CHANGE_WARNING", AlertSeverity.Warning)]
    [InlineData(5.0, null, null)] // below threshold, no alert
    public void Evaluate_PriceChange_ShouldTriggerCorrectRule(double changePct, string? expectedRule, AlertSeverity? expectedSeverity)
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-003",
            ChangeType = VarianceChangeType.Modified,
            Dimension = VarianceDimension.Price,
            ChangePercent = changePct,
            OldValue = "100",
            NewValue = (100 * (1 + changePct / 100)).ToString("F0")
        };

        var alerts = _evaluator.Evaluate(new[] { variance });

        if (expectedRule == null)
            alerts.Should().BeEmpty();
        else
        {
            alerts.Should().HaveCount(1);
            alerts[0].TriggeredRule.Should().Be(expectedRule);
            alerts[0].Severity.Should().Be(expectedSeverity);
        }
    }

    [Fact]
    public void Evaluate_LargeQuantityChange_ShouldTriggerRule()
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-004",
            ChangeType = VarianceChangeType.Modified,
            Dimension = VarianceDimension.BomStructure,
            ChangePercent = 60.0,
            OldValue = "1.000",
            NewValue = "1.600"
        };

        var alerts = _evaluator.Evaluate(new[] { variance });

        alerts.Should().HaveCount(1);
        alerts[0].TriggeredRule.Should().Be("BOM_QTY_LARGE_CHANGE");
        alerts[0].Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public void Evaluate_SmallQuantityChange_ShouldNotTriggerRule()
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-005",
            ChangeType = VarianceChangeType.Modified,
            Dimension = VarianceDimension.BomStructure,
            ChangePercent = 30.0
        };

        var alerts = _evaluator.Evaluate(new[] { variance });
        alerts.Should().BeEmpty(); // 30% is under 50% threshold
    }

    [Fact]
    public void Evaluate_MultipleVariances_ShouldReturnAllMatchingAlerts()
    {
        var variances = new[]
        {
            new VarianceResult { NodeCode = "A", ChangeType = VarianceChangeType.Removed, Dimension = VarianceDimension.BomStructure },
            new VarianceResult { NodeCode = "B", ChangeType = VarianceChangeType.Added, Dimension = VarianceDimension.BomStructure },
            new VarianceResult { NodeCode = "C", ChangeType = VarianceChangeType.Modified, Dimension = VarianceDimension.Price, ChangePercent = 50.0 }
        };

        var alerts = _evaluator.Evaluate(variances);

        alerts.Should().HaveCount(3);
    }

    [Fact]
    public void Evaluate_UnchangedNode_ShouldNotTriggerAnyAlert()
    {
        var variance = new VarianceResult
        {
            NodeCode = "MAT-006",
            ChangeType = VarianceChangeType.Unchanged,
            Dimension = VarianceDimension.BomStructure
        };

        var alerts = _evaluator.Evaluate(new[] { variance });
        alerts.Should().BeEmpty();
    }
}
