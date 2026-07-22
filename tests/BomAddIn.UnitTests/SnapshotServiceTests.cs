using System;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class SnapshotServiceTests
{
    private readonly Mock<IDataSnapshotRepository> _snapshotRepoMock = new();
    private readonly Mock<IDbConnectionFactory> _connFactoryMock = new();
    private readonly Mock<IAuthorizationService> _authzMock = new();
    private readonly SnapshotService _service;

    public SnapshotServiceTests()
    {
        _service = new SnapshotService(_snapshotRepoMock.Object, _connFactoryMock.Object, _authzMock.Object);
    }

    [Fact]
    public void Compare_SameSnapshot_ReturnsResult()
    {
        var data = "{\n  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
                   "  \"Materials\": {\n    \"MAT-001\":{\"id\":1}\n  }\n}";
        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        result.SnapshotIdA.Should().Be(1);
        result.SnapshotIdB.Should().Be(2);
        result.UnchangedCounts["Materials"].Should().Be(1);
        result.AddedCounts.Should().BeEmpty();
        result.RemovedCounts.Should().BeEmpty();
    }

    [Fact]
    public void Compare_DifferentSizes_DetectsDifference()
    {
        var snapA = new DataSnapshot { Id = 1, SnapshotData = "{\n" +
            "  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
            "  \"Materials\": {\n    \"MAT-001\":{\"id\":1},\n    \"MAT-002\":{\"id\":2},\n    \"MAT-003\":{\"id\":3}\n  }\n}" };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = "{\n" +
            "  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
            "  \"Materials\": {\n    \"MAT-001\":{\"id\":1}\n  }\n}" };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        result.RemovedCounts["Materials"].Should().Be(2); // 3 entries vs 1 entry
        result.AddedCounts.Should().BeEmpty();
        result.UnchangedCounts.Should().BeEmpty();
    }

    [Fact]
    public void Compare_NotFound_Throws()
    {
        _snapshotRepoMock.Setup(r => r.GetById(999)).Returns((DataSnapshot?)null);

        Action act = () => _service.Compare(999, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CleanupOldSnapshots_DelegatesToRepository()
    {
        _service.CleanupOldSnapshots(UserRole.Admin, 30);

        _snapshotRepoMock.Verify(r => r.DeleteOlderThan(
            It.Is<DateTime>(d => d <= DateTime.UtcNow.AddDays(-30)),
            null), Times.Once);
    }

    [Fact]
    public void GetRecent_WithType_DelegatesToRepository()
    {
        _snapshotRepoMock.Setup(r => r.GetByType("Manual", 5))
            .Returns(new[] { new DataSnapshot { Id = 1 }, new DataSnapshot { Id = 2 } });

        var results = _service.GetRecent("Manual", 5);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void GetRecent_AllTypes_CombinesResults()
    {
        _snapshotRepoMock.Setup(r => r.GetByType("Daily", 10))
            .Returns(new[] { new DataSnapshot { Id = 1, CreatedAt = DateTime.UtcNow } });
        _snapshotRepoMock.Setup(r => r.GetByType("Manual", 10))
            .Returns(new[] { new DataSnapshot { Id = 2, CreatedAt = DateTime.UtcNow.AddHours(-1) } });

        var results = _service.GetRecent(limit: 10);

        results.Should().HaveCount(2);
    }

    // ═══ H-3 回归: ParseSnapshotTables 边缘用例测试 ═══
    // 验证新的字符级状态机解析器正确性

    [Fact]
    public void Compare_EmptyTable_CountsZero()
    {
        var data = "{\n  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
                   "  \"Materials\": {\n  }\n}";
        var snap = new DataSnapshot { Id = 1, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snap);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snap);

        var result = _service.Compare(1, 2);

        result.UnchangedCounts["Materials"].Should().Be(0);
        result.AddedCounts.Should().BeEmpty();
        result.RemovedCounts.Should().BeEmpty();
    }

    [Fact]
    public void Compare_MultipleTables_CountsCorrectly()
    {
        var data = "{\n" +
            "  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
            "  \"Materials\": {\n    \"MAT-001\":{\"id\":1},\n    \"MAT-002\":{\"id\":2}\n  },\n" +
            "  \"BomStructures\": {\n    \"1\":{\"parentId\":1},\n    \"2\":{\"parentId\":2},\n    \"3\":{\"parentId\":3}\n  }\n}";
        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        result.UnchangedCounts["Materials"].Should().Be(2);
        result.UnchangedCounts["BomStructures"].Should().Be(3);
    }

    [Fact]
    public void Compare_EscapedQuotesInValues_ParsedCorrectly()
    {
        // 物料名称含双引号 → EscapeJsonString 转义为 \\\"
        var data = "{\n" +
            "  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
            "  \"Materials\": {\n" +
            "    \"MAT-001\":{\"id\":1,\"name\":\"Steel 10\\\\\\\" Plate\"},\n" +
            "    \"MAT-002\":{\"id\":2,\"name\":\"Normal\"}\n" +
            "  }\n}";
        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        // 不应被转义引号干扰计数
        result.UnchangedCounts["Materials"].Should().Be(2);
    }

    [Fact]
    public void Compare_ColonInValues_NotMisidentifiedAsTableHeader()
    {
        // H-3 核心场景: 值中含有 ": 字符不应被误判为表头
        // warehouse "WH:01" — 冒号在行内容中
        var data = "{\n" +
            "  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",\n" +
            "  \"Inventories\": {\n" +
            "    \"1\":{\"materialId\":1,\"warehouse\":\"WH:01\",\"qty\":100},\n" +
            "    \"2\":{\"materialId\":2,\"warehouse\":\"WH:02\",\"qty\":200}\n" +
            "  }\n}";
        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        // 不应被 "WH:01" 中的 " 误判为表头；应正确计数 2 个条目
        result.UnchangedCounts["Inventories"].Should().Be(2);
    }

    [Fact]
    public void Compare_TabsAndExtraWhitespace_HandledCorrectly()
    {
        // 制表符 + 换行 \r\n 替换 \n
        var data = "{\r\n" +
            "\t\"capturedAt\": \"2026-07-13T00:00:00.0000000\",\r\n" +
            "\t\"Materials\": {\r\n" +
            "\t\t\"MAT-001\":{\"id\":1,\"orgId\":1,\"name\":\"A\",\"spec\":\"100mm\",\"unit\":\"pcs\",\"category\":\"Raw\"},\r\n" +
            "\t\t\"MAT-002\":{\"id\":2,\"orgId\":1,\"name\":\"B\",\"spec\":\"200mm\",\"unit\":\"pcs\",\"category\":\"Raw\"}\r\n" +
            "\t}\r\n" +
            "}";
        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        result.UnchangedCounts["Materials"].Should().Be(2);
    }

    [Fact]
    public void Compare_SingleTableWithManyEntries_CountsAccurately()
    {
        // 批量压力测试: 100 个条目
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"capturedAt\": \"2026-07-13T00:00:00.0000000\",");
        sb.AppendLine("  \"Materials\": {");
        for (int i = 1; i <= 100; i++)
        {
            sb.Append($"    \"MAT-{i:D4}\":{{\"id\":{i},\"name\":\"Item{i}\"}}");
            sb.AppendLine(i < 100 ? "," : "");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        var data = sb.ToString();

        var snapA = new DataSnapshot { Id = 1, SnapshotData = data };
        var snapB = new DataSnapshot { Id = 2, SnapshotData = data };
        _snapshotRepoMock.Setup(r => r.GetById(1)).Returns(snapA);
        _snapshotRepoMock.Setup(r => r.GetById(2)).Returns(snapB);

        var result = _service.Compare(1, 2);

        result.UnchangedCounts["Materials"].Should().Be(100);
    }
}
