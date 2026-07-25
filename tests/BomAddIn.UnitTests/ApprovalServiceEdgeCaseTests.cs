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

/// <summary>
/// ApprovalService 边界和错误路径测试 — 补充审查发现的测试缺口：
///   无效状态转换（每个方法 + 错误当前状态）
///   C-5 TOCTOU 修复验证（自审批检查在事务内）
/// </summary>
public class ApprovalServiceEdgeCaseTests
{
    private readonly Mock<IBomVersionRepository> _versionRepoMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<IDbConnectionFactory> _connFactoryMock = new();
    private readonly Mock<IDbConnection> _connMock = new();
    private readonly Mock<IDbTransaction> _txMock = new();
    private readonly ApprovalService _service;

    public ApprovalServiceEdgeCaseTests()
    {
        _connFactoryMock.Setup(f => f.CreateConnection()).Returns(_connMock.Object);
        _connMock.Setup(c => c.BeginTransaction()).Returns(_txMock.Object);
        _service = new ApprovalService(_versionRepoMock.Object, _auditMock.Object, _connFactoryMock.Object,
            Mock.Of<IAuthorizationService>());
    }

    /// <summary>创建指定状态的版本 mock</summary>
    private void SetupVersion(long id, VersionState state, long? approvedBy = null)
    {
        var version = new BomVersion
        {
            Id = id,
            BomId = 100,
            VersionNumber = 1,
            State = state,
            ApprovedBy = approvedBy,
            CreatedAt = DateTime.UtcNow
        };
        _versionRepoMock.Setup(r => r.GetById(id, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(version);
    }

    #region SubmitForReview — 无效状态

    [Fact]
    public void SubmitForReview_FromApproved_ThrowsInvalidTransition()
    {
        SetupVersion(1, VersionState.Approved);

        Action act = () => _service.SubmitForReview(1, UserRole.Admin, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void SubmitForReview_FromReleased_ThrowsInvalidTransition()
    {
        SetupVersion(1, VersionState.Released);

        Action act = () => _service.SubmitForReview(1, UserRole.Admin, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void SubmitForReview_FromObsolete_ThrowsInvalidTransition()
    {
        SetupVersion(1, VersionState.Obsolete);

        Action act = () => _service.SubmitForReview(1, UserRole.Admin, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    #endregion

    #region Approve — 无效状态 + 自审批

    [Fact]
    public void Approve_FromDraft_ThrowsInvalidTransition()
    {
        SetupVersion(2, VersionState.Draft);

        Action act = () => _service.Approve(2, userId: 101, callerRole: UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void Approve_FromApproved_ThrowsInvalidTransition()
    {
        SetupVersion(2, VersionState.Approved);

        Action act = () => _service.Approve(2, userId: 101, callerRole: UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void Approve_SelfApproval_Throws()
    {
        // ApprovedBy 在 PendingReview 状态时存储提交人 ID
        // 如果审批人 userId == 提交人 ApprovedBy → 应拒绝
        SetupVersion(2, VersionState.PendingReview, approvedBy: 101);

        Action act = () => _service.Approve(2, userId: 101, callerRole: UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*自行审批*");
    }

    [Fact]
    public void Approve_InvalidUserId_Throws()
    {
        Action act = () => _service.Approve(1, userId: 0, callerRole: UserRole.Admin);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*审批人 ID*");
    }

    [Fact]
    public void Approve_NegativeUserId_Throws()
    {
        Action act = () => _service.Approve(1, userId: -1, callerRole: UserRole.Admin);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*审批人 ID*");
    }

    #endregion

    #region Reject — 无效状态

    [Fact]
    public void Reject_FromDraft_ThrowsInvalidTransition()
    {
        SetupVersion(3, VersionState.Draft);

        Action act = () => _service.Reject(3, userId: 102, callerRole: UserRole.Admin, "Test rejection reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void Reject_FromApproved_ThrowsInvalidTransition()
    {
        SetupVersion(3, VersionState.Approved);

        Action act = () => _service.Reject(3, userId: 102, callerRole: UserRole.Admin, "Test rejection reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    #endregion

    #region Release — 无效状态

    [Fact]
    public void Release_FromDraft_ThrowsInvalidTransition()
    {
        SetupVersion(4, VersionState.Draft);

        Action act = () => _service.Release(4, UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void Release_FromPendingReview_ThrowsInvalidTransition()
    {
        SetupVersion(4, VersionState.PendingReview);

        Action act = () => _service.Release(4, UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    #endregion

    #region Obsolete — 全部状态允许

    [Theory]
    [InlineData(VersionState.Draft)]
    [InlineData(VersionState.PendingReview)]
    [InlineData(VersionState.Approved)]
    [InlineData(VersionState.Rejected)]
    [InlineData(VersionState.Released)]
    public void Obsolete_FromAnyState_Succeeds(VersionState from)
    {
        SetupVersion(5, from);

        // Obsolete 从任何非终态都可以转换
        var result = _service.Obsolete(5, UserRole.Admin);

        _versionRepoMock.Verify(r => r.UpdateState(5, VersionState.Obsolete, null, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    [Fact]
    public void Obsolete_FromObsolete_ThrowsInvalidTransition()
    {
        SetupVersion(5, VersionState.Obsolete);

        Action act = () => _service.Obsolete(5, UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid state transition*");
    }

    #endregion

    #region IsValidTransition — 全矩阵验证

    [Theory]
    [InlineData(VersionState.Draft, VersionState.PendingReview, true)]
    [InlineData(VersionState.Draft, VersionState.Obsolete, true)]
    [InlineData(VersionState.Draft, VersionState.Approved, false)]
    [InlineData(VersionState.Draft, VersionState.Released, false)]
    [InlineData(VersionState.PendingReview, VersionState.Approved, true)]
    [InlineData(VersionState.PendingReview, VersionState.Rejected, true)]
    [InlineData(VersionState.PendingReview, VersionState.Obsolete, true)]
    [InlineData(VersionState.PendingReview, VersionState.Draft, false)]
    [InlineData(VersionState.Approved, VersionState.Released, true)]
    [InlineData(VersionState.Approved, VersionState.Obsolete, true)]
    [InlineData(VersionState.Approved, VersionState.Draft, false)]
    [InlineData(VersionState.Rejected, VersionState.Draft, true)]
    [InlineData(VersionState.Rejected, VersionState.Obsolete, true)]
    [InlineData(VersionState.Rejected, VersionState.Approved, false)]
    [InlineData(VersionState.Released, VersionState.Obsolete, true)]
    [InlineData(VersionState.Released, VersionState.Draft, false)]
    [InlineData(VersionState.Obsolete, VersionState.Draft, false)]
    [InlineData(VersionState.Obsolete, VersionState.Approved, false)]
    public void IsValidTransition_AllScenarios(VersionState from, VersionState to, bool expected)
    {
        _service.IsValidTransition(from, to).Should().Be(expected);
    }

    #endregion

    #region VersionNotFound

    [Fact]
    public void Approve_VersionNotFound_Throws()
    {
        _versionRepoMock.Setup(r => r.GetById(999, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns((BomVersion?)null);

        Action act = () => _service.Approve(999, userId: 100, callerRole: UserRole.Admin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void SubmitForReview_VersionNotFound_Throws()
    {
        _versionRepoMock.Setup(r => r.GetById(999, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns((BomVersion?)null);

        Action act = () => _service.SubmitForReview(999, UserRole.Admin, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    #endregion
}
