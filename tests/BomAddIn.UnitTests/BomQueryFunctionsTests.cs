using System;
using System.Collections.Generic;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.UDF;
using BomAddIn.UDF.Functions;
using ExcelDna.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// Unit tests for BomQueryFunctions: BOMEXPAND and BOMCOST UDFs.
/// Each test sets up its own DI container with mocked services via Container.Initialize().
/// </summary>
[Collection("UDF")]
public class BomQueryFunctionsTests : IDisposable
{
    private readonly Mock<IBomService> _bomServiceMock = new();
    private readonly Mock<IMaterialRepository> _materialRepoMock = new();
    private readonly ServiceProvider _provider;

    public BomQueryFunctionsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_bomServiceMock.Object);
        services.AddScoped(_ => _materialRepoMock.Object);
        _provider = services.BuildServiceProvider();
        Container.Initialize(_provider);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // BOMEXPAND
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BomExpand_NullOrEmptyItemCode_ReturnsNA()
    {
        // Arrange — no service call expected for empty/null itemCode

        // Act
        var result = BomQueryFunctions.BomExpand("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void BomExpand_NullItemCode_ReturnsNA()
    {
        // Act
        var result = BomQueryFunctions.BomExpand(null!);

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void BomExpand_ItemNotFound_ReturnsNA()
    {
        // Arrange
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime?>()))
            .Returns(new List<BomExpandedNode>());

        // Act
        var result = BomQueryFunctions.BomExpand("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
        _bomServiceMock.Verify(s => s.Expand("MAT-001", It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public void BomExpand_WithResults_ReturnsRectangularArray()
    {
        // Arrange
        var nodes = new List<BomExpandedNode>
        {
            new() { Level = 0, ItemCode = "MAT-001", Description = "Assembly", Quantity = 1.0, Unit = "EA", Source = "Make" },
            new() { Level = 1, ItemCode = "MAT-002", Description = "Component", Quantity = 2.5, Unit = "KG", Source = "Buy" },
        };
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime?>()))
            .Returns(nodes);

        // Act
        var result = BomQueryFunctions.BomExpand("MAT-001");

        // Assert
        result.Should().BeAssignableTo<object[,]>();

        var arr = (object[,])result;
        // 2 data rows + 1 header row = 3 rows, 6 columns
        arr.GetLength(0).Should().Be(3);
        arr.GetLength(1).Should().Be(6);

        // Header row
        arr[0, 0].Should().Be("Level");
        arr[0, 1].Should().Be("ItemCode");
        arr[0, 2].Should().Be("Description");
        arr[0, 3].Should().Be("Quantity");
        arr[0, 4].Should().Be("Unit");
        arr[0, 5].Should().Be("Source");

        // Data row 1
        arr[1, 0].Should().Be(0);
        arr[1, 1].Should().Be("MAT-001");
        arr[1, 2].Should().Be("Assembly");
        arr[1, 3].Should().Be(1.0);
        arr[1, 4].Should().Be("EA");
        arr[1, 5].Should().Be("Make");

        // Data row 2
        arr[2, 0].Should().Be(1);
        arr[2, 1].Should().Be("MAT-002");
        arr[2, 2].Should().Be("Component");
        arr[2, 3].Should().Be(2.5);
        arr[2, 4].Should().Be("KG");
        arr[2, 5].Should().Be("Buy");
    }

    [Fact]
    public void BomExpand_NonReleasedVersionState_ReturnsValueError()
    {
        // Arrange — F-01 fix: 版本检查在 Expand 调用之前，无需 Mock Expand

        // Act
        var result = BomQueryFunctions.BomExpand("MAT-001", versionState: "Draft");

        // Assert — version != "Released" returns #VALUE! (unsupported parameter, distinct from #N/A = not found)
        result.Should().Be(ExcelError.ExcelErrorValue);
        // Expand 不应被调用（版本检查前移）
        _bomServiceMock.Verify(s => s.Expand(It.IsAny<string>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public void BomExpand_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime?>()))
            .Throws(new InvalidOperationException("Database connection failed"));

        // Act
        var result = BomQueryFunctions.BomExpand("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }

    // ═══════════════════════════════════════════════════════════════
    // BOMCOST
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BomCost_NullOrEmptyItemCode_ReturnsNA()
    {
        // Act
        var result = BomQueryFunctions.BomCost("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void BomCost_NullItemCode_ReturnsNA()
    {
        // Act
        var result = BomQueryFunctions.BomCost(null!);

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void BomCost_ItemNotFound_ReturnsNA()
    {
        // Arrange — CalculateCost returns 0 (item not found internally),
        // and MaterialRepository confirms item does not exist
        _bomServiceMock
            .Setup(s => s.CalculateCost("MAT-001", It.IsAny<DateTime?>()))
            .Returns(0.0);
        _materialRepoMock
            .Setup(r => r.GetByCode(1, "MAT-001"))
            .Returns((Material?)null);

        // Act
        var result = BomQueryFunctions.BomCost("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void BomCost_WithValidItem_ReturnsCost()
    {
        // Arrange
        var nodes = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Level = 0 },
        };
        _bomServiceMock
            .Setup(s => s.Expand("MAT-001", It.IsAny<DateTime?>()))
            .Returns(nodes);
        _bomServiceMock
            .Setup(s => s.CalculateCost("MAT-001", It.IsAny<DateTime?>()))
            .Returns(42.5);

        // Act
        var result = BomQueryFunctions.BomCost("MAT-001");

        // Assert
        result.Should().Be(42.5);
    }

    [Fact]
    public void BomCost_ZeroCost_ReturnsZero()
    {
        // Arrange — valid item but cost is exactly 0: returns 0.0 (not NA)
        _bomServiceMock
            .Setup(s => s.CalculateCost("MAT-001", It.IsAny<DateTime?>()))
            .Returns(0.0);
        _materialRepoMock
            .Setup(r => r.GetByCode(1, "MAT-001"))
            .Returns(new Material { Id = 1, Code = "MAT-001" });

        // Act
        var result = BomQueryFunctions.BomCost("MAT-001");

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void BomCost_Exception_ReturnsExcelErrorValue()
    {
        // Arrange — CalculateCost throws (U-2 fix: 现在直接调用 CalculateCost)
        _bomServiceMock
            .Setup(s => s.CalculateCost("MAT-001", It.IsAny<DateTime?>()))
            .Throws(new InvalidOperationException("Database connection failed"));

        // Act
        var result = BomQueryFunctions.BomCost("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }
}
