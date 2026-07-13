using System;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Infrastructure.Security;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IUserTokenRepository> _tokenRepoMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(_userRepoMock.Object, _tokenRepoMock.Object, _hasherMock.Object);
    }

    private static User CreateUser(string username = "testuser", string role = "Admin")
    {
        return new User
        {
            Id = 1, Username = username, PasswordHash = "hashed_pw",
            Role = role == "Admin" ? UserRole.Admin : role == "Analyst" ? UserRole.Analyst : UserRole.Viewer,
            OrgId = 1, IsActive = true, FailedLoginAttempts = 0, LockoutUntil = null
        };
    }

    [Fact]
    public void Authenticate_Success_ReturnsToken()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        _hasherMock.Setup(h => h.Verify("correct", "hashed_pw")).Returns(true);

        var result = _authService.Authenticate("testuser", "correct");

        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Authenticate_WrongPassword_Fails()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        _hasherMock.Setup(h => h.Verify("wrong", "hashed_pw")).Returns(false);

        var result = _authService.Authenticate("testuser", "wrong");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Authenticate_WrongPassword_IncrementsFailedCount()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        _userRepoMock.Setup(r => r.IncrementAndLockIfNeeded(1, 5, It.IsAny<string>())).Returns(1);
        _hasherMock.Setup(h => h.Verify("wrong", "hashed_pw")).Returns(false);

        _authService.Authenticate("testuser", "wrong");

        _userRepoMock.Verify(r => r.IncrementAndLockIfNeeded(1, 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Authenticate_FiveFailures_LocksAccount()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 4;
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        _userRepoMock.Setup(r => r.IncrementAndLockIfNeeded(1, 5, It.IsAny<string>())).Returns(5);
        _hasherMock.Setup(h => h.Verify("wrong", "hashed_pw")).Returns(false);

        var result = _authService.Authenticate("testuser", "wrong");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("锁定");
    }

    [Fact]
    public void Authenticate_LockedAccount_RejectsCorrectPassword()
    {
        var user = CreateUser();
        user.LockoutUntil = DateTime.UtcNow.AddMinutes(10);
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);

        var result = _authService.Authenticate("testuser", "correct");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("锁定");
    }

    [Fact]
    public void Authenticate_LockoutExpired_AllowsLogin()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 5;
        user.LockoutUntil = DateTime.UtcNow.AddMinutes(-10);
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        _hasherMock.Setup(h => h.Verify("correct", "hashed_pw")).Returns(true);

        var result = _authService.Authenticate("testuser", "correct");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Authenticate_UserNotFound_Fails()
    {
        _userRepoMock.Setup(r => r.GetByUsername("nobody")).Returns((User?)null);
        var result = _authService.Authenticate("nobody", "any");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Authenticate_InactiveUser_Fails()
    {
        var user = CreateUser();
        user.IsActive = false;
        _userRepoMock.Setup(r => r.GetByUsername("testuser")).Returns(user);
        var result = _authService.Authenticate("testuser", "correct");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Logout_RevokesAllTokens()
    {
        _authService.Logout(1);
        _tokenRepoMock.Verify(r => r.RevokeAllForUser(1), Times.Once);
    }

    [Fact]
    public void SeedAdminUser_FirstRun_CreatesAdmin()
    {
        _userRepoMock.Setup(r => r.GetByUsername("admin")).Returns((User?)null);
        _authService.SeedAdminUser();
        _userRepoMock.Verify(r => r.Add(It.Is<User>(u =>
            u.Username == "admin" && u.Role == UserRole.Admin)), Times.Once);
    }

    [Fact]
    public void SeedAdminUser_Idempotent_DoesNotCreateDuplicate()
    {
        _userRepoMock.Setup(r => r.GetByUsername("admin")).Returns(CreateUser("admin"));
        _authService.SeedAdminUser();
        _userRepoMock.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
    }
}
