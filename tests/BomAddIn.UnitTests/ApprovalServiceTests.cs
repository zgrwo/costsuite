using System;
using System.Data;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

[Trait("Category", "Unit")]
public class ApprovalServiceTests
{
    private readonly Mock<IBomVersionRepository> _versionRepoMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<IDbConnectionFactory> _connectionFactoryMock;
    private readonly Mock<IDbConnection> _connectionMock;
    private readonly Mock<IDbTransaction> _transactionMock;
    private readonly ApprovalService _service;

    public ApprovalServiceTests()
    {
        _versionRepoMock = new Mock<IBomVersionRepository>();
        _auditServiceMock = new Mock<IAuditService>();
        _connectionFactoryMock = new Mock<IDbConnectionFactory>();
        _connectionMock = new Mock<IDbConnection>();
        _transactionMock = new Mock<IDbTransaction>();
        _connectionFactoryMock.Setup(f => f.CreateConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(c => c.BeginTransaction()).Returns(_transactionMock.Object);

        // B-4 fix: 设置事务内 GetById 重载的默认 Mock（各测试可按需覆盖）
        _versionRepoMock
            .Setup(r => r.GetById(It.IsAny<long>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns((long id, IDbConnection c, IDbTransaction t) => _versionRepoMock.Object.GetById(id));

        _auditServiceMock
            .Setup(a => a.Log(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(),
                It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .Callback(() => { });

        _service = new ApprovalService(_versionRepoMock.Object, _auditServiceMock.Object,
            _connectionFactoryMock.Object);
    }

    [Fact]
    public void SubmitForReview_Draft_ShouldSucceed()
    {
        var version = new BomVersion { Id = 1, State = VersionState.Draft };
        _versionRepoMock.Setup(r => r.GetById(1)).Returns(version);

        var result = _service.SubmitForReview(1, null);

        _versionRepoMock.Verify(r => r.UpdateState(1, VersionState.PendingReview, null,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Approve_PendingReview_ShouldSucceed()
    {
        var version = new BomVersion { Id = 2, State = VersionState.PendingReview };
        _versionRepoMock.Setup(r => r.GetById(2)).Returns(version);

        var result = _service.Approve(2, 100);

        _versionRepoMock.Verify(r => r.UpdateState(2, VersionState.Approved, (long?)100,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _auditServiceMock.Verify(a => a.Log(
            "APPROVE", "BomVersions", 2, null, It.IsAny<string>(), (long?)100), Times.Once);
    }

    [Fact]
    public void Reject_PendingReview_ShouldSucceed()
    {
        var version = new BomVersion { Id = 3, State = VersionState.PendingReview };
        _versionRepoMock.Setup(r => r.GetById(3)).Returns(version);

        var result = _service.Reject(3, 200, "Needs revision");

        _versionRepoMock.Verify(r => r.UpdateState(3, VersionState.Rejected, (long?)200,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    [Fact]
    public void Release_Approved_ShouldSucceed()
    {
        var version = new BomVersion { Id = 4, State = VersionState.Approved };
        _versionRepoMock.Setup(r => r.GetById(4)).Returns(version);

        var result = _service.Release(4, null);

        _versionRepoMock.Verify(r => r.UpdateState(4, VersionState.Released, null,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    [Fact]
    public void Obsolete_FromAnyState_ShouldSucceed()
    {
        var version = new BomVersion { Id = 5, State = VersionState.Released };
        _versionRepoMock.Setup(r => r.GetById(5)).Returns(version);

        var result = _service.Obsolete(5, null);

        _versionRepoMock.Verify(r => r.UpdateState(5, VersionState.Obsolete, null,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    [Theory]
    [InlineData(VersionState.Released, VersionState.Draft)]      // cannot go back
    [InlineData(VersionState.Released, VersionState.PendingReview)]
    [InlineData(VersionState.Approved, VersionState.Draft)]
    [InlineData(VersionState.Obsolete, VersionState.Draft)]       // terminal state
    [InlineData(VersionState.Obsolete, VersionState.Released)]
    public void InvalidTransition_ShouldThrow(VersionState from, VersionState to)
    {
        _service.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void VersionNotFound_ShouldThrow()
    {
        _versionRepoMock.Setup(r => r.GetById(999)).Returns((BomVersion?)null);

        Action act = () => _service.SubmitForReview(999, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void Resubmit_Rejected_ShouldReturnToDraft()
    {
        var version = new BomVersion { Id = 6, State = VersionState.Rejected };
        _versionRepoMock.Setup(r => r.GetById(6)).Returns(version);

        var result = _service.Resubmit(6, null);

        _versionRepoMock.Verify(r => r.UpdateState(6, VersionState.Draft, null,
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    [Fact]
    public void Draft_CanObsolete_Directly()
    {
        _service.IsValidTransition(VersionState.Draft, VersionState.Obsolete).Should().BeTrue();
    }

    [Fact]
    public void Rejected_CanObsolete_Directly()
    {
        _service.IsValidTransition(VersionState.Rejected, VersionState.Obsolete).Should().BeTrue();
    }

    [Fact]
    public void SubmittedForReview_FromDraft_IsValid()
    {
        _service.IsValidTransition(VersionState.Draft, VersionState.PendingReview).Should().BeTrue();
    }

    [Fact]
    public void AuditedOnApprove()
    {
        var version = new BomVersion { Id = 10, State = VersionState.PendingReview };
        _versionRepoMock.Setup(r => r.GetById(10)).Returns(version);

        _service.Approve(10, 42, "LGTM");

        _auditServiceMock.Verify(a => a.Log(
            It.Is<string>(s => s == "APPROVE"),
            It.Is<string>(t => t == "BomVersions"),
            It.Is<long>(id => id == 10),
            null,
            It.Is<string>(v => v!.Contains("LGTM")),
            (long?)42), Times.Once);
    }

    [Fact]
    public void AuditedOnReject()
    {
        var version = new BomVersion { Id = 11, State = VersionState.PendingReview };
        _versionRepoMock.Setup(r => r.GetById(11)).Returns(version);

        _service.Reject(11, 42, "Bad data");

        _auditServiceMock.Verify(a => a.Log(
            It.Is<string>(s => s == "REJECT"),
            It.Is<string>(t => t == "BomVersions"),
            It.Is<long>(id => id == 11),
            null,
            It.IsAny<string>(),
            (long?)42), Times.Once);
    }
}
