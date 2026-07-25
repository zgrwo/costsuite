using System;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

public class AuthorizationServiceTests
{
    private readonly AuthorizationService _authz = new();

    [Fact]
    public void Admin_HasAllOperations()
    {
        foreach (BomOperation op in Enum.GetValues(typeof(BomOperation)))
            _authz.Authorize(UserRole.Admin, op).Should().BeTrue($"Admin should have {op}");
    }

    [Fact]
    public void Analyst_CanReadMaterials()
    {
        _authz.Authorize(UserRole.Analyst, BomOperation.MaterialRead).Should().BeTrue();
    }

    [Fact]
    public void Analyst_CanCreateMaterials()
    {
        _authz.Authorize(UserRole.Analyst, BomOperation.MaterialCreate).Should().BeTrue();
    }

    [Fact]
    public void Analyst_CannotDeleteMaterials()
    {
        _authz.Authorize(UserRole.Analyst, BomOperation.MaterialDelete).Should().BeFalse();
    }

    [Fact]
    public void Analyst_CannotManageUsers()
    {
        _authz.Authorize(UserRole.Analyst, BomOperation.UserManage).Should().BeFalse();
    }

    [Fact]
    public void Analyst_CanTriggerSync()
    {
        _authz.Authorize(UserRole.Analyst, BomOperation.SyncTrigger).Should().BeTrue();
    }

    [Fact]
    public void Viewer_CanRead_Only()
    {
        _authz.Authorize(UserRole.Viewer, BomOperation.MaterialRead).Should().BeTrue();
        _authz.Authorize(UserRole.Viewer, BomOperation.BomRead).Should().BeTrue();
        _authz.Authorize(UserRole.Viewer, BomOperation.SupplierRead).Should().BeTrue();
        _authz.Authorize(UserRole.Viewer, BomOperation.ConfigRead).Should().BeTrue();
        _authz.Authorize(UserRole.Viewer, BomOperation.MaterialCreate).Should().BeFalse();
        _authz.Authorize(UserRole.Viewer, BomOperation.SyncTrigger).Should().BeFalse();
    }

    [Fact]
    public void Demand_ShouldThrow_WhenUnauthorized()
    {
        Action act = () => _authz.Demand(UserRole.Viewer, BomOperation.MaterialDelete);
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Demand_ShouldNotThrow_WhenAuthorized()
    {
        Action act = () => _authz.Demand(UserRole.Admin, BomOperation.MaterialDelete);
        act.Should().NotThrow();
    }

    [Fact]
    public void IsAdmin_ReturnsTrue_ForAdmin()
    {
        _authz.IsAdmin(UserRole.Admin).Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_ForAnalyst()
    {
        _authz.IsAdmin(UserRole.Analyst).Should().BeFalse();
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_ForViewer()
    {
        _authz.IsAdmin(UserRole.Viewer).Should().BeFalse();
    }

    [Fact]
    public void AllOperations_AreDefined()
    {
        // 18 operations as defined in BomOperation enum
        Enum.GetValues(typeof(BomOperation)).Length.Should().BeGreaterThanOrEqualTo(15);
    }
}
