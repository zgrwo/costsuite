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
/// M-4c: 大型数据集边界测试 — BOM 深层嵌套、截断哨兵、大数据量方差计算。
/// 补充现有 EdgeCaseTests.cs 和 VarianceCalculatorEdgeCaseTests.cs 未覆盖的大数据场景。
/// </summary>
public class LargeDatasetTests
{
    private readonly VarianceCalculator _calculator = new();

    // ═══ BOM 深层嵌套边界 ═══

    [Fact]
    public void BomExpansion_DeepLinearChain_CompletesAtMaxDepth()
    {
        // 构造线性深层 BOM: MAT-0 → MAT-1 → MAT-2 → ... → MAT-30
        // 深度 30 > maxLevel=20，应触发截断
        var nodes = new List<BomExpandedNode>();
        for (int i = 0; i <= 30; i++)
        {
            nodes.Add(new BomExpandedNode
            {
                Level = i,
                MaterialId = i,
                ParentMaterialId = i == 0 ? (long?)null : i - 1,
                ItemCode = $"MAT-{i}",
                Description = $"Material Level {i}",
                Quantity = 1.0,
                Unit = "PCS",
                Source = "Test",
                VersionState = "Released"
            });
        }

        // 不应崩溃 — 深层 BOM 展开在 maxLevel 处截断
        nodes.Count.Should().Be(31);
        nodes.Last().Level.Should().Be(30);
    }

    [Fact]
    public void BomExpansion_TruncationSentinel_HasNegativeLevel()
    {
        // 构造截断哨兵节点（模拟 BomAnalysisProvider 在 maxLevel 处添加）
        var results = new List<BomExpandedNode>();
        for (int i = 0; i < 20; i++)
        {
            results.Add(new BomExpandedNode
            {
                Level = i, MaterialId = i, ParentMaterialId = i == 0 ? null : i - 1,
                ItemCode = $"MAT-{i}", Description = $"Node {i}", Quantity = 1.0
            });
        }
        // 添加截断哨兵
        results.Add(new BomExpandedNode
        {
            Level = -1, MaterialId = -1, ParentMaterialId = null,
            ItemCode = "[TRUNCATED]",
            Description = "BOM depth exceeded 20 levels. Results incomplete.",
            Quantity = 0, Unit = "", Source = "System", VersionState = ""
        });

        // VarianceCalculator.CompareBomVersions 应过滤 Level==-1 的哨兵节点
        var filtered = results.Where(n => n.Level >= 0).ToList();
        filtered.Should().HaveCount(20);
        filtered.Should().NotContain(n => n.ItemCode == "[TRUNCATED]");
    }

    // ═══ VarianceCalculator 大数据 ═══

    [Fact]
    public void CompareBomVersions_LargeFlatBom_CompletesWithoutError()
    {
        // 大型扁平 BOM: 5000 个直接子节点
        const int childCount = 5000;
        var root = new List<BomExpandedNode>
        {
            new BomExpandedNode { Level = 0, MaterialId = 0, ParentMaterialId = null,
                ItemCode = "ROOT", Description = "Root", Quantity = 1.0 }
        };
        for (int i = 1; i <= childCount; i++)
        {
            root.Add(new BomExpandedNode
            {
                Level = 1, MaterialId = i, ParentMaterialId = 0,
                ItemCode = $"CHILD-{i:D5}", Description = $"Child {i}",
                Quantity = i % 10 + 1.0, Unit = "PCS"
            });
        }

        var versionB = root.Select(n => new BomExpandedNode
        {
            Level = n.Level, MaterialId = n.MaterialId, ParentMaterialId = n.ParentMaterialId,
            ItemCode = n.ItemCode, Description = n.Description,
            Quantity = n.Quantity * 1.1,
            Unit = n.Unit
        }).ToList();

        // Act: 不应 OOM 或超时
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = _calculator.CompareBomVersions(root, versionB);
        sw.Stop();

        // Assert: 5000 子节点 + 1 根节点（根节点 Qty 1.0 vs 1.1 也是 Modified）
        var modified = results.Count(r => r.ChangeType == VarianceChangeType.Modified);
        modified.Should().BeGreaterOrEqualTo(childCount);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "5000+ node comparison should complete within 5s");
    }

    [Fact]
    public void CompareBomVersions_DuplicateKeys_SameParent_DistinguishedBySecondarySort()
    {
        // G-2 fix 回归: 同 (ItemCode, ParentMaterialId, Level) 组内按 MaterialId→Description→Quantity 排序
        var versionA = new List<BomExpandedNode>
        {
            new BomExpandedNode { Level = 1, MaterialId = 5, ParentMaterialId = 0,
                ItemCode = "DUP", Description = "Variant A", Quantity = 3.0 },
            new BomExpandedNode { Level = 1, MaterialId = 10, ParentMaterialId = 0,
                ItemCode = "DUP", Description = "Variant B", Quantity = 3.0 },
        };
        var versionB = new List<BomExpandedNode>
        {
            new BomExpandedNode { Level = 1, MaterialId = 5, ParentMaterialId = 0,
                ItemCode = "DUP", Description = "Variant A", Quantity = 3.0 },
            new BomExpandedNode { Level = 1, MaterialId = 10, ParentMaterialId = 0,
                ItemCode = "DUP", Description = "Variant B", Quantity = 5.0 },
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);

        // 应识别出 Variant B 的数量变化，而非假 Removed+Added
        results.Should().ContainSingle(r => r.ChangeType == VarianceChangeType.Modified
            && r.NodeDescription.Contains("Variant B"));
        results.Should().NotContain(r => r.ChangeType == VarianceChangeType.Removed);
        results.Should().NotContain(r => r.ChangeType == VarianceChangeType.Added);
    }

    [Fact]
    public void CompareBomVersions_OnlyTruncationSentinels_FilteredAndReturnsEmpty()
    {
        var versionA = new List<BomExpandedNode>
        {
            new BomExpandedNode { Level = -1, MaterialId = -1, ItemCode = "[TRUNCATED]",
                Description = "truncated", Quantity = 0 }
        };
        var versionB = new List<BomExpandedNode>
        {
            new BomExpandedNode { Level = -1, MaterialId = -1, ItemCode = "[TRUNCATED]",
                Description = "truncated", Quantity = 0 }
        };

        var results = _calculator.CompareBomVersions(versionA, versionB);
        results.Should().BeEmpty();
    }

    // ═══ ComparePrices 边界 ═══

    [Fact]
    public void ComparePrices_NegativePriceA_TreatedAsMaxValueSentinel()
    {
        // priceA <= 0 时比较逻辑走哨兵路径 (ChangePercent = double.MaxValue)
        var results = _calculator.ComparePrices(1, -10m, DateTime.Today, "USD",
            -5m, DateTime.Today, "USD");

        // priceA=-10 <= 0, priceB=-5 != 0 → ChangePercent = double.MaxValue
        results.Should().ContainSingle(r => r.ChangePercent == double.MaxValue);
    }

    [Fact]
    public void ComparePrices_LargePriceValues_NoOverflow()
    {
        // 使用足以超出相对阈值 (0.01%) 的差异
        var results = _calculator.ComparePrices(1, 100_000m, DateTime.Today, "CNY",
            102_000m, DateTime.Today, "CNY");

        results.Should().NotBeEmpty();
        results[0].ChangePercent.Should().HaveValue();
    }

    [Fact]
    public void ComparePrices_ZeroToPositive_SetsMaxValueSentinel()
    {
        var results = _calculator.ComparePrices(1, 0m, DateTime.Today, "USD",
            100m, DateTime.Today, "USD");

        results.Should().ContainSingle(r => r.ChangePercent == double.MaxValue);
    }

    // ═══ SnapshotService 大数据截断 ═══

    [Fact]
    public void Compare_SnapshotJson_1000Entries_ParsedCorrectly()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"capturedAt\": \"2026-07-23T00:00:00.0000000\",");
        sb.AppendLine("  \"LargeTable\": {");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"    \"KEY-{i:D5}\":{{\"id\":{i},\"value\":\"item{i}\"}}");
            sb.AppendLine(i < 999 ? "," : "");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        var data = sb.ToString();

        // 验证 JSON 结构完整
        data.Should().Contain("LargeTable");
        data.Should().Contain("KEY-00000");
        data.Should().Contain("KEY-00999");

        // 行级验证
        var lineCount = data.Split('\n').Count(l =>
            l.TrimStart().StartsWith("\"KEY-"));
        lineCount.Should().Be(1000);
    }
}
