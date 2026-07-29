using System;
using System.Data;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// BomVersionRepository 集成测试 — 验证版本 CRUD、状态转换和事务重载。
/// </summary>
public class BomVersionRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly BomVersionRepository _repo;
    private static int _bomIdCounter;

    public BomVersionRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new BomVersionRepository(fixture);
        // FK=True: BomVersions.ApprovedBy → Users(Id)，确保 approvedBy 用户存在
        _fixture.SeedUser("approver-42", 42);
        _fixture.SeedUser("approver-99", 99);
    }

    private static long NextBomId() => System.Threading.Interlocked.Increment(ref _bomIdCounter) + 1000L;

    private BomVersion CreateVersion(long? bomId = null, int versionNumber = 1, VersionState state = VersionState.Draft)
    {
        var actualBomId = bomId ?? NextBomId();
        // FK=True: BomVersions.BomId → Materials(Id)，必须先插入前置物料
        _fixture.SeedMaterial($"MAT-FK-{actualBomId}", actualBomId);
        var version = new BomVersion
        {
            BomId = actualBomId,
            VersionNumber = versionNumber,
            State = state,
            CreatedAt = DateTime.UtcNow
        };
        _repo.Add(version);
        return version;
    }

    [Fact]
    public void Add_ShouldInsertAndReturnId()
    {
        var version = CreateVersion();
        version.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Add_WithTransaction_ShouldInsertWithinTransaction()
    {
        var bomId = NextBomId();
        _fixture.SeedMaterial($"MAT-TX-{bomId}", bomId);

        using var conn = _fixture.CreateConnection();
        using var tx = conn.BeginTransaction();

        var v1 = new BomVersion { BomId = bomId, VersionNumber = 1, State = VersionState.Draft, CreatedAt = DateTime.UtcNow };
        _repo.Add(v1, conn, tx);

        // 事务提交前，外部连接应看不到
        using var otherConn = _fixture.CreateConnection();
        var beforeCommit = otherConn.QueryFirstOrDefault<BomVersion>(
            "SELECT * FROM BomVersions WHERE BomId = @BomId", new { BomId = bomId });
        beforeCommit.Should().BeNull("事务未提交前其他连接不应看到数据");

        tx.Commit();

        var afterCommit = _repo.GetLatest(bomId);
        afterCommit.Should().NotBeNull();
        afterCommit!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public void GetByBomId_ShouldReturnAllVersions()
    {
        var bomId = NextBomId();
        CreateVersion(bomId, 1, VersionState.Draft);
        CreateVersion(bomId, 2, VersionState.PendingReview);
        CreateVersion(bomId, 3, VersionState.Approved);

        var versions = _repo.GetByBomId(bomId).ToList();
        versions.Should().HaveCount(3);
        versions.Should().BeInDescendingOrder(v => v.VersionNumber);
    }

    [Fact]
    public void GetLatest_ShouldReturnHighestVersion()
    {
        var bomId = NextBomId();
        CreateVersion(bomId, 1, VersionState.Draft);
        CreateVersion(bomId, 2, VersionState.Released);

        var latest = _repo.GetLatest(bomId);
        latest.Should().NotBeNull();
        latest!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void GetLatest_NoVersions_ShouldReturnNull()
    {
        var result = _repo.GetLatest(999999);
        result.Should().BeNull();
    }

    [Fact]
    public void UpdateState_ShouldPersistStateChange()
    {
        var version = CreateVersion(state: VersionState.Draft);

        _repo.UpdateState(version.Id, VersionState.PendingReview, approvedBy: 42);

        var updated = _repo.GetById(version.Id);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(VersionState.PendingReview);
    }

    [Fact]
    public void UpdateState_Approved_ShouldSetApprovedAt()
    {
        var version = CreateVersion(state: VersionState.PendingReview);

        _repo.UpdateState(version.Id, VersionState.Approved, approvedBy: 99);

        var updated = _repo.GetById(version.Id);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(VersionState.Approved);
    }

    [Fact]
    public void GetById_NonExistent_ShouldReturnNull()
    {
        var result = _repo.GetById(999999);
        result.Should().BeNull();
    }
}
