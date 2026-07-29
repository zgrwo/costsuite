using System;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class AuditLogRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly AuditLogRepository _repo;

    public AuditLogRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new AuditLogRepository(fixture);
        // FK=True: AuditLogs.UserId → Users(Id)，确保测试中使用的 UserId 存在
        _fixture.SeedUser("audit-test-user", 1);
        _fixture.SeedUser("audit-user-42", 42);
        _fixture.SeedUser("audit-user-99", 99);
    }

    [Fact]
    public void Add_ShouldInsertAndReturnId()
    {
        var log = new AuditLog
        {
            UserId = 1,
            Action = AuditAction.Create,
            TableName = "Materials",
            RecordId = 100,
            OldValues = null,
            NewValues = "{\"Name\":\"Test\"}",
            Timestamp = DateTime.UtcNow
        };

        _repo.Add(log);
        log.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetByTable_ShouldReturnFilteredResults()
    {
        _repo.Add(new AuditLog { UserId = 1, Action = AuditAction.Update, TableName = "Materials", RecordId = 1,
            Timestamp = DateTime.UtcNow.AddHours(-1) });
        _repo.Add(new AuditLog { UserId = 1, Action = AuditAction.Delete, TableName = "Materials", RecordId = 2,
            Timestamp = DateTime.UtcNow });
        _repo.Add(new AuditLog { UserId = 1, Action = AuditAction.Create, TableName = "BomStructures", RecordId = 3,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetByTable("Materials", DateTime.UtcNow.AddHours(-2)).ToList();
        results.Should().HaveCount(2);
    }

    [Fact]
    public void GetByUser_ShouldReturnUserOperations()
    {
        _repo.Add(new AuditLog { UserId = 42, Action = AuditAction.Update, TableName = "Prices", RecordId = 1,
            Timestamp = DateTime.UtcNow });
        _repo.Add(new AuditLog { UserId = 99, Action = AuditAction.Delete, TableName = "Prices", RecordId = 2,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetByUser(42, 10).ToList();
        results.Should().HaveCount(1);
        results[0].Action.Should().Be(AuditAction.Update);
    }

    [Fact]
    public void GetRecent_ShouldReturnLatestFirst()
    {
        var uniqueTable = "RecentTest_" + Guid.NewGuid().ToString("N")[..8];
        _repo.Add(new AuditLog { UserId = 1, Action = AuditAction.Create, TableName = uniqueTable, RecordId = 1,
            Timestamp = DateTime.UtcNow.AddMinutes(-5) });
        _repo.Add(new AuditLog { UserId = 1, Action = AuditAction.Update, TableName = uniqueTable, RecordId = 2,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetRecent(100).ToList();
        var myEntries = results.Where(r => r.TableName == uniqueTable).ToList();
        myEntries.Should().HaveCount(2);
        myEntries[0].Action.Should().Be(AuditAction.Update); // 最新在前
    }
}
