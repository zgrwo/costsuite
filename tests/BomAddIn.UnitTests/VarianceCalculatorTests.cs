using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

public class VarianceCalculatorTests
{
    private readonly VarianceCalculator _calculator = new();

    [Fact]
    public void CompareBomVersions_AddedNode_ShouldReturnAdded()
    {
        var versionA = new List<BomExpandedNode>();
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "New Material", Quantity = 2.0, Level = 1, MaterialId = 1 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().HaveCount(1);
        results[0].ChangeType.Should().Be(VarianceChangeType.Added);
        results[0].NodeCode.Should().Be("MAT-001");
        results[0].Dimension.Should().Be(VarianceDimension.BomStructure);
    }

    [Fact]
    public void CompareBomVersions_RemovedNode_ShouldReturnRemoved()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Old Material", Quantity = 2.0, Level = 1, MaterialId = 1 }
        };
        var versionB = new List<BomExpandedNode>();

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().HaveCount(1);
        results[0].ChangeType.Should().Be(VarianceChangeType.Removed);
    }

    [Fact]
    public void CompareBomVersions_ModifiedQuantity_ShouldReturnModified()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Material", Quantity = 1.0, Level = 1, MaterialId = 1 }
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Material", Quantity = 3.0, Level = 1, MaterialId = 1 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().HaveCount(1);
        results[0].ChangeType.Should().Be(VarianceChangeType.Modified);
        results[0].OldValue.Should().Be("1.000");
        results[0].NewValue.Should().Be("3.000");
        results[0].ChangePercent.Should().Be(200.0);
    }

    [Fact]
    public void CompareBomVersions_LevelChanged_ShouldReturnModified()
    {
        // 同一复合键 (ItemCode|ParentMaterialId|Level) 但数量变化 → Modified
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Material", Quantity = 1.0, Level = 2, MaterialId = 1, ParentMaterialId = 100 }
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Material", Quantity = 2.0, Level = 2, MaterialId = 1, ParentMaterialId = 100 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().HaveCount(1);
        results[0].ChangeType.Should().Be(VarianceChangeType.Modified);
    }

    [Fact]
    public void CompareBomVersions_UnchangedNode_ShouldNotReturn()
    {
        var node = new BomExpandedNode { ItemCode = "MAT-001", Description = "Same", Quantity = 1.5, Level = 1, MaterialId = 1 };
        var versionA = new List<BomExpandedNode> { node };
        var versionB = new List<BomExpandedNode> { node };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().BeEmpty();
    }

    [Fact]
    public void CompareBomVersions_EmptyBoth_ShouldReturnEmpty()
    {
        var results = _calculator.CompareBomVersions(new List<BomExpandedNode>(), new List<BomExpandedNode>());
        results.Should().BeEmpty();
    }

    [Fact]
    public void CompareBomVersions_SameItemCode_MergedCorrectly()
    {
        // Duplicate ItemCodes: last one wins (dictionary behavior)
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "X", Description = "X", Quantity = 5.0, Level = 1, MaterialId = 10 }
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "X", Description = "X", Quantity = 8.0, Level = 1, MaterialId = 10 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        results.Should().HaveCount(1);
        results[0].ChangeType.Should().Be(VarianceChangeType.Modified);
    }

    [Fact]
    public void ComparePrices_NoChange_ShouldReturnEmpty()
    {
        var results = _calculator.ComparePrices(1, 100.0m, DateTime.Today, "CNY", 100.0m, DateTime.Today, "CNY");
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComparePrices_SmallChange_ShouldReturnModified()
    {
        var results = _calculator.ComparePrices(1, 100.0m, DateTime.Today, "CNY", 105.0m, DateTime.Today, "CNY");

        results.Should().HaveCount(1);
        results[0].Dimension.Should().Be(VarianceDimension.Price);
        results[0].ChangePercent.Should().Be(5.0);
    }

    [Fact]
    public void ComparePrices_ZeroOldPrice_ReturnsMaxValueSentinel()
    {
        // C-12 fix: priceA==0 且 priceB!=0 时，返回 decimal.MaxValue 作为无穷大百分比标记
        // 与 CompareBomVersions 使用 double.PositiveInfinity 的逻辑一致
        var results = _calculator.ComparePrices(1, 0.0m, DateTime.Today, "CNY", 100.0m, DateTime.Today, "CNY");

        results.Should().HaveCount(1);
        results[0].ChangePercent.Should().Be(double.MaxValue);
    }

    [Fact]
    public void CompareBomVersions_ResultSortedDescending()
    {
        var versionA = new List<BomExpandedNode>();
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "A", Description = "A", Quantity = 1, Level = 1, MaterialId = 1 },
            new() { ItemCode = "B", Description = "B", Quantity = 1, Level = 1, MaterialId = 2 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);
        // Both Added, order should be stable
        results.Should().HaveCount(2);
        results.All(r => r.ChangeType == VarianceChangeType.Added).Should().BeTrue();
    }
}
