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
}
