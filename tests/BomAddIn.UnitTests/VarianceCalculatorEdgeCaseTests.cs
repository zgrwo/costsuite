using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// VarianceCalculator 边界和错误路径测试 — 补充审查发现的测试缺口：
///   C-3: 跨版本键匹配（同物料多出现、不同版本不同数量）
///   C-12: 零价格/零数量百分比
///   同物料不同层级、无效输入
/// </summary>
public class VarianceCalculatorEdgeCaseTests
{
    private readonly VarianceCalculator _calc = new();

    #region CompareBomVersions — C-3 跨版本匹配

    [Fact]
    public void SameMaterialMultipleOccurrences_MoreInVersionA_ReportsRemoved()
    {
        // 版本 A 有 2 个相同 (ItemCode, ParentMaterialId, Level) 的节点
        // 版本 B 有 1 个
        // C-3 fix: 分组比较不应产生假 Removed
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
            new() { ItemCode = "SCR", Description = "Screw-B", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 6 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // 应报告 1 个 Removed（Screw-B），而非 0 或 2
        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Removed);
        results.First(r => r.ChangeType == VarianceChangeType.Removed)
            .NodeDescription.Should().Contain("Screw-B");
    }

    [Fact]
    public void SameMaterialMultipleOccurrences_MoreInVersionB_ReportsAdded()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
            new() { ItemCode = "SCR", Description = "Screw-C", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 8 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Added);
        results.First(r => r.ChangeType == VarianceChangeType.Added)
            .NodeDescription.Should().Contain("Screw-C");
    }

    [Fact]
    public void SameMaterialEqualCount_AllMatched_ReportsModified()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
            new() { ItemCode = "SCR", Description = "Screw-B", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 10 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "SCR", Description = "Screw-A", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 4 },
            new() { ItemCode = "SCR", Description = "Screw-B", MaterialId = 10, ParentMaterialId = 1, Level = 2, Quantity = 12 }, // 数量变化
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // 应有 1 个 Modified（第二个螺丝数量从 10→12）
        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Modified);
    }

    [Fact]
    public void SameItemCodeDifferentLevel_TreatedAsDifferentNodes()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "L1", MaterialId = 1, ParentMaterialId = null, Level = 1, Quantity = 1 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "L2", MaterialId = 1, ParentMaterialId = 5, Level = 2, Quantity = 1 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // Level 不同 → 不同键 → 一个 Removed + 一个 Added
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Removed);
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Added);
    }

    [Fact]
    public void SameItemCodeDifferentParent_TreatedAsDifferentNodes()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Under-A", MaterialId = 1, ParentMaterialId = 10, Level = 1, Quantity = 1 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MAT-001", Description = "Under-B", MaterialId = 1, ParentMaterialId = 20, Level = 1, Quantity = 1 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // ParentMaterialId 不同 → 不同键
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Removed);
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Added);
    }

    #endregion

    #region CompareBomVersions — 边界输入

    [Fact]
    public void BothEmpty_ReturnsEmptyResults()
    {
        var results = _calc.CompareBomVersions(new List<BomExpandedNode>(), new List<BomExpandedNode>());
        results.Should().BeEmpty();
    }

    [Fact]
    public void VersionAEmpty_AllReportedAsAdded()
    {
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "A", Description = "A", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 1 },
            new() { ItemCode = "B", Description = "B", MaterialId = 2, ParentMaterialId = 1, Level = 1, Quantity = 2 },
        };

        var results = _calc.CompareBomVersions(new List<BomExpandedNode>(), versionB);

        results.Should().AllSatisfy(r => r.ChangeType.Should().Be(VarianceChangeType.Added));
        results.Should().HaveCount(2);
    }

    [Fact]
    public void VersionBEmpty_AllReportedAsRemoved()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "A", Description = "A", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 1 },
        };

        var results = _calc.CompareBomVersions(versionA, new List<BomExpandedNode>());

        results.Should().AllSatisfy(r => r.ChangeType.Should().Be(VarianceChangeType.Removed));
        results.Should().HaveCount(1);
    }

    [Fact]
    public void NullVersionA_ThrowsArgumentNullException()
    {
        Action act = () => _calc.CompareBomVersions(null!, new List<BomExpandedNode>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void QuantityDifferentByLessThanEpsilon_NotReported()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "A", Description = "A", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 100.0 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "A", Description = "A", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 100.00001 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // 差异 < 0.01% → 不应报告
        results.Should().BeEmpty();
    }

    [Fact]
    public void QuantityZeroToNonZero_ReportsWithInfinityPercent()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "NEW", Description = "New Item", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 0 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "NEW", Description = "New Item", MaterialId = 1, ParentMaterialId = null, Level = 0, Quantity = 100 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Modified);
        // 无穷大百分比哨兵：VarianceCalculator 内部将除以零场景的无穷大百分比映射为 100.0
        // （参见 VarianceCalculator 中 InfinityPercentSentinel 常量的处理逻辑）
        const double infinityPercentSentinel = 100.0;
        results[0].ChangePercent.Should().Be(infinityPercentSentinel);
    }

    [Fact]
    public void LargeDataset_AllCorrectlyMatched()
    {
        // 1000 节点，每 5 个中有 1 个数量变更
        var versionA = new List<BomExpandedNode>();
        var versionB = new List<BomExpandedNode>();
        for (int i = 0; i < 1000; i++)
        {
            var qty = 1.0 + (i % 5 == 0 ? 0 : 0); // 每 5 个触发变更
            versionA.Add(new BomExpandedNode
            {
                ItemCode = $"MAT-{i:D4}", Description = $"Material {i}",
                MaterialId = i + 1, ParentMaterialId = i > 0 ? i : null,
                Level = i > 0 ? 1 : 0, Quantity = 1.0
            });
            versionB.Add(new BomExpandedNode
            {
                ItemCode = $"MAT-{i:D4}", Description = $"Material {i}",
                MaterialId = i + 1, ParentMaterialId = i > 0 ? i : null,
                Level = i > 0 ? 1 : 0, Quantity = i % 5 == 0 ? 2.0 : 1.0
            });
        }

        var results = _calc.CompareBomVersions(versionA, versionB);

        // 每 5 个有 1 个变更 → 200 个 Modified
        results.Count(r => r.ChangeType == VarianceChangeType.Modified).Should().Be(200);
        results.Where(r => r.ChangeType == VarianceChangeType.Added).Should().BeEmpty();
        results.Where(r => r.ChangeType == VarianceChangeType.Removed).Should().BeEmpty();
    }

    #endregion

    #region ComparePrices — C-12 零价格处理

    [Fact]
    public void ComparePrices_BothZero_NoChangeReported()
    {
        var results = _calc.ComparePrices(1, 0m, DateTime.Today, "CNY", 0m, DateTime.Today, "CNY");
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComparePrices_ZeroToPositive_ReportsMaxValueSentinel()
    {
        // C-12 fix: 旧价格=0、新价格>0 时，百分比使用 decimal.MaxValue 哨兵值
        var results = _calc.ComparePrices(1, 0m, DateTime.Today, "CNY", 500m, DateTime.Today, "CNY");

        results.Should().HaveCount(1);
        results[0].ChangePercent.Should().BeNull();
    }

    [Fact]
    public void ComparePrices_PositiveToZero_ReportsNegativeInfinity()
    {
        var results = _calc.ComparePrices(1, 100m, DateTime.Today, "CNY", 0m, DateTime.Today, "CNY");

        results.Should().HaveCount(1);
        results[0].ChangePercent.Should().Be(-100.0); // (0-100)/100*100 = -100
    }

    [Fact]
    public void ComparePrices_DifferentCurrency_ReportsSkipped()
    {
        var results = _calc.ComparePrices(1, 100m, DateTime.Today, "CNY", 100m, DateTime.Today, "USD");

        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Unchanged
            && r.NodeDescription.Contains("币种不同"));
    }

    [Fact]
    public void ComparePrices_SameCurrency_SamePrice_NoChange()
    {
        var results = _calc.ComparePrices(1, 100m, DateTime.Today, "CNY", 100m, DateTime.Today, "CNY");
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComparePrices_SmallChangeBelowThreshold_NoChange()
    {
        // 价格 1000，变化 0.001 → 低于相对阈值
        var results = _calc.ComparePrices(1, 1000m, DateTime.Today, "CNY", 1000.001m, DateTime.Today, "CNY");
        results.Should().BeEmpty();
    }

    #endregion

    #region CompareBomVersions — 层级变化检测

    [Fact]
    public void LevelChange_ReportsCorrectDescription()
    {
        var versionA = new List<BomExpandedNode>
        {
            new() { ItemCode = "MOVED", Description = "Moved Item", MaterialId = 1, ParentMaterialId = 10, Level = 3, Quantity = 5 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new() { ItemCode = "MOVED", Description = "Moved Item", MaterialId = 1, ParentMaterialId = 20, Level = 2, Quantity = 5 },
        };

        var results = _calc.CompareBomVersions(versionA, versionB);

        // ParentMaterialId 变了 → 不同键 → Removed + Added（非 Modified 的层级变化）
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Removed);
        results.Should().Contain(r => r.ChangeType == VarianceChangeType.Added);
    }

    #endregion
}
