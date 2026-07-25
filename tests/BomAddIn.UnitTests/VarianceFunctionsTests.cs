using System;
using System.Collections.Generic;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.UDF;
using BomAddIn.UDF.Functions;
using ExcelDna.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// Unit tests for VarianceFunctions: VARIANCECHECK and ALERTCHECK UDFs.
/// </summary>
[Collection("UDF")]
public class VarianceFunctionsTests : IDisposable
{
    private readonly Mock<IBomService> _bomServiceMock = new();
    private readonly Mock<IVarianceService> _varianceServiceMock = new();
    private readonly Mock<IAlertEvaluator> _alertEvaluatorMock = new();
    private readonly Mock<IVarianceCalculator> _varianceCalculatorMock = new();
    private readonly Mock<IPriceRecordRepository> _priceRepoMock = new();
    private readonly ServiceProvider _provider;

    public VarianceFunctionsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_bomServiceMock.Object);
        services.AddSingleton(_varianceServiceMock.Object);
        services.AddSingleton(_alertEvaluatorMock.Object);
        services.AddSingleton(_varianceCalculatorMock.Object);
        services.AddSingleton(_priceRepoMock.Object);
        _provider = services.BuildServiceProvider();
        Container.Initialize(_provider);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // VARIANCECHECK
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VarianceCheck_EmptyItemCode_ReturnsNA()
    {
        // Act
        var result = VarianceFunctions.VarianceCheck("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void VarianceCheck_NoVariances_ReturnsNA()
    {
        // Arrange
        var emptyNodes = new List<BomExpandedNode>();
        _bomServiceMock
            .Setup(s => s.Expand(It.IsAny<string>(), It.IsAny<DateTime?>()))
            .Returns(emptyNodes);

        var emptyResult = new VarianceAnalysisResult
        {
            StructureVariances = new List<VarianceResult>(),
            PriceVariances = new List<VarianceResult>(),
        };
        _varianceServiceMock
            .Setup(s => s.RunFullAnalysis(It.IsAny<List<BomExpandedNode>>(), It.IsAny<DateTime>(),
                It.IsAny<List<BomExpandedNode>>(), It.IsAny<DateTime>()))
            .Returns(emptyResult);

        // Act
        var result = VarianceFunctions.VarianceCheck("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void VarianceCheck_WithVariances_ReturnsArray()
    {
        // Arrange
        var nodes = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Level = 0 },
            new() { ItemCode = "MAT-002", Level = 1 },
        };
        _bomServiceMock
            .Setup(s => s.Expand(It.IsAny<string>(), It.IsAny<DateTime?>()))
            .Returns(nodes);

        var analysisResult = new VarianceAnalysisResult
        {
            StructureVariances = new List<VarianceResult>
            {
                new()
                {
                    NodeCode = "MAT-002",
                    ChangeType = VarianceChangeType.Removed,
                    Dimension = VarianceDimension.BomStructure,
                    OldValue = "1.0",
                    NewValue = null,
                },
            },
            PriceVariances = new List<VarianceResult>
            {
                new()
                {
                    NodeCode = "MAT-001",
                    ChangeType = VarianceChangeType.Modified,
                    Dimension = VarianceDimension.Price,
                    OldValue = "100.00",
                    NewValue = "120.00",
                },
            },
        };
        _varianceServiceMock
            .Setup(s => s.RunFullAnalysis(It.IsAny<List<BomExpandedNode>>(), It.IsAny<DateTime>(),
                It.IsAny<List<BomExpandedNode>>(), It.IsAny<DateTime>()))
            .Returns(analysisResult);

        // Act
        var result = VarianceFunctions.VarianceCheck("MAT-001", asOfDateB: DateTime.Today);

        // Assert
        result.Should().BeAssignableTo<object[,]>();
        var arr = (object[,])result;

        // 2 variances + 1 header = 3 rows, 5 columns
        arr.GetLength(0).Should().Be(3);
        arr.GetLength(1).Should().Be(5);

        // Header
        arr[0, 0].Should().Be("NodeCode");
        arr[0, 1].Should().Be("ChangeType");
        arr[0, 2].Should().Be("Dimension");
        arr[0, 3].Should().Be("OldValue");
        arr[0, 4].Should().Be("NewValue");

        // Row 1: Structure variance (Removed) — NewValue is null → empty string in array
        arr[1, 0].Should().Be("MAT-002");
        arr[1, 1].Should().Be("Removed");
        arr[1, 2].Should().Be("BomStructure");
        arr[1, 3].Should().Be("1.0");
        arr[1, 4].Should().Be("");

        // Row 2: Price variance (Modified)
        arr[2, 0].Should().Be("MAT-001");
        arr[2, 1].Should().Be("Modified");
        arr[2, 2].Should().Be("Price");
        arr[2, 3].Should().Be("100.00");
        arr[2, 4].Should().Be("120.00");
    }

    [Fact]
    public void VarianceCheck_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _bomServiceMock
            .Setup(s => s.Expand(It.IsAny<string>(), It.IsAny<DateTime?>()))
            .Throws(new InvalidOperationException("Service unavailable"));

        // Act
        var result = VarianceFunctions.VarianceCheck("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }

    // ═══════════════════════════════════════════════════════════════
    // ALERTCHECK
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AlertCheck_EmptyOrNullItemCode_ReturnsNA()
    {
        // Arrange — evaluator resolves but returns empty (no BOM expansion for null/empty code)
        _alertEvaluatorMock
            .Setup(e => e.Evaluate(It.IsAny<IEnumerable<VarianceResult>>()))
            .Returns(new List<Alert>());

        // Act — pass null
        var result = VarianceFunctions.AlertCheck(null);

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void AlertCheck_EmptyItemCode_ReturnsNA()
    {
        // Arrange
        _alertEvaluatorMock
            .Setup(e => e.Evaluate(It.IsAny<IEnumerable<VarianceResult>>()))
            .Returns(new List<Alert>());

        // Act — pass empty string
        var result = VarianceFunctions.AlertCheck("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void AlertCheck_WithAlerts_ReturnsArray()
    {
        // Arrange — simulate a full pipeline: Expand → GetHistoryBatch → ComparePrices → Evaluate
        var nodes = new List<BomExpandedNode>
        {
            new() { MaterialId = 1, ItemCode = "MAT-001", Level = 0 },
            new() { MaterialId = 2, ItemCode = "MAT-002", Level = 1 },
        };
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime>()))
            .Returns(nodes);

        // Two price records per material (need >= 2 for ComparePrices)
        var priceHistory = new List<PriceRecord>
        {
            new() { MaterialId = 1, UnitPrice = 90m, EffectiveDate = DateTime.Today.AddMonths(-2), Currency = "CNY" },
            new() { MaterialId = 1, UnitPrice = 100m, EffectiveDate = DateTime.Today.AddMonths(-1), Currency = "CNY" },
            new() { MaterialId = 2, UnitPrice = 45m, EffectiveDate = DateTime.Today.AddMonths(-2), Currency = "CNY" },
            new() { MaterialId = 2, UnitPrice = 50m, EffectiveDate = DateTime.Today.AddMonths(-1), Currency = "CNY" },
        };
        _priceRepoMock
            .Setup(r => r.GetHistoryBatch(It.IsAny<IEnumerable<long>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(priceHistory);

        // ComparePrices returns variance for each material
        _varianceCalculatorMock
            .Setup(c => c.ComparePrices(
                It.IsAny<long>(),
                It.IsAny<decimal>(), It.IsAny<DateTime>(), "CNY",
                It.IsAny<decimal>(), It.IsAny<DateTime>(), "CNY"))
            .Returns(new List<VarianceResult>
            {
                new()
                {
                    NodeCode = "MAT-001",
                    ChangeType = VarianceChangeType.Modified,
                    Dimension = VarianceDimension.Price,
                    OldValue = "90",
                    NewValue = "100",
                },
            });

        // Evaluator returns alerts
        _alertEvaluatorMock
            .Setup(e => e.Evaluate(It.IsAny<IEnumerable<VarianceResult>>()))
            .Returns(new List<Alert>
            {
                new()
                {
                    Severity = AlertSeverity.Warning,
                    Message = "价格变化超过 10%",
                    TriggeredRule = "PriceChangeRule",
                    NodeCode = "MAT-001",
                },
                new()
                {
                    Severity = AlertSeverity.Error,
                    Message = "成本超预算",
                    TriggeredRule = "BudgetRule",
                    NodeCode = null,
                },
            });

        // Act
        var result = VarianceFunctions.AlertCheck("MAT-001");

        // Assert
        result.Should().BeAssignableTo<object[,]>();
        var arr = (object[,])result;

        // 2 alerts + 1 header = 3 rows, 4 columns
        arr.GetLength(0).Should().Be(3);
        arr.GetLength(1).Should().Be(4);

        // Header
        arr[0, 0].Should().Be("Severity");
        arr[0, 1].Should().Be("Message");
        arr[0, 2].Should().Be("Rule");
        arr[0, 3].Should().Be("NodeCode");

        // Row 1
        arr[1, 0].Should().Be("Warning");
        arr[1, 1].Should().Be("价格变化超过 10%");
        arr[1, 2].Should().Be("PriceChangeRule");
        arr[1, 3].Should().Be("MAT-001");

        // Row 2 — NodeCode is null → empty string in array
        arr[2, 0].Should().Be("Error");
        arr[2, 1].Should().Be("成本超预算");
        arr[2, 2].Should().Be("BudgetRule");
        arr[2, 3].Should().Be("");
    }

    [Fact]
    public void AlertCheck_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime>()))
            .Throws(new InvalidOperationException("Service unavailable"));

        // Act
        var result = VarianceFunctions.AlertCheck("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }
}
