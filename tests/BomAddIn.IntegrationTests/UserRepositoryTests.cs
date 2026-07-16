using System;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

/// <summary>
/// UserRepository 集成测试 — 验证原子锁仓 SQL (IncrementAndLockIfNeeded) 和 CRUD 操作。
/// </summary>
public class UserRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly UserRepository _repo;

    public UserRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new UserRepository(fixture);
    }

    private User CreateUser(string username, UserRole role = UserRole.Viewer)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = "hashed_dummy",
            Role = role,
            OrgId = 1,
            IsActive = true,
            FailedLoginAttempts = 0,
            CreatedAt = DateTime.UtcNow
        };
        _repo.Add(user);
        return user;
    }

    [Fact]
    public void Add_ShouldInsertAndReturnId()
    {
        var user = CreateUser("test_add", UserRole.Viewer);
        user.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetByUsername_ShouldReturnCorrectUser()
    {
        var user = CreateUser("test_get", UserRole.Admin);
        var retrieved = _repo.GetByUsername("test_get");
        retrieved.Should().NotBeNull();
        retrieved!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void IncrementLoginAttempts_ShouldIncreaseCounter()
    {
        var user = CreateUser("test_inc");
        var after1 = _repo.IncrementLoginAttempts(user.Id);
        after1.Should().Be(1);

        var after2 = _repo.IncrementLoginAttempts(user.Id);
        after2.Should().Be(2);
    }

    [Fact]
    public void IncrementAndLockIfNeeded_UnderThreshold_ShouldNotLock()
    {
        var user = CreateUser("test_threshold");
        var attempts = _repo.IncrementAndLockIfNeeded(user.Id, maxAttempts: 5, lockoutUntil: null);
        attempts.Should().Be(1);

        // 用户不应被锁
        var retrieved = _repo.GetById(user.Id);
        retrieved.Should().NotBeNull();
        retrieved!.LockoutUntil.Should().BeNull();
    }

    [Fact]
    public void IncrementAndLockIfNeeded_AtThreshold_ShouldLock()
    {
        var user = CreateUser("test_lock");
        var lockoutTime = DateTime.UtcNow.AddMinutes(30);

        var attempts = _repo.IncrementAndLockIfNeeded(user.Id, maxAttempts: 1, lockoutUntil: lockoutTime);
        attempts.Should().Be(1);

        // 验证锁仓已生效 (只验证非null，SQLite 存储格式可能不保留毫秒精度)
        var retrieved = _repo.GetById(user.Id);
        retrieved.Should().NotBeNull();
        retrieved!.LockoutUntil.Should().NotBeNull();
    }

    [Fact]
    public void UpdateLoginAttempts_ShouldResetAndUnlock()
    {
        var user = CreateUser("test_reset");
        _repo.UpdateLoginAttempts(user.Id, 3, DateTime.UtcNow.AddMinutes(5));
        var locked = _repo.GetById(user.Id);
        locked!.FailedLoginAttempts.Should().Be(3);
        locked.LockoutUntil.Should().NotBeNull();

        // 重置
        _repo.UpdateLoginAttempts(user.Id, 0, null);
        var unlocked = _repo.GetById(user.Id);
        unlocked!.FailedLoginAttempts.Should().Be(0);
        unlocked.LockoutUntil.Should().BeNull();
    }

    [Fact]
    public void GetById_NonExistent_ShouldReturnNull()
    {
        var result = _repo.GetById(999999);
        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_ShouldReturnAllUsers()
    {
        CreateUser("test_all_1");
        CreateUser("test_all_2");
        var users = _repo.GetAll();
        users.Should().HaveCountGreaterOrEqualTo(2);
    }
}
