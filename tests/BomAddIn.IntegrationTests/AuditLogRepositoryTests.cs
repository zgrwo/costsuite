using System;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
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
    }

    [Fact]
    public void Add_ShouldInsertAndReturnId()
    {
        var log = new AuditLog
        {
            UserId = 1,
            Action = "CREATE",
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
        _repo.Add(new AuditLog { UserId = 1, Action = "UPDATE", TableName = "Materials", RecordId = 1,
            Timestamp = DateTime.UtcNow.AddHours(-1) });
        _repo.Add(new AuditLog { UserId = 1, Action = "DELETE", TableName = "Materials", RecordId = 2,
            Timestamp = DateTime.UtcNow });
        _repo.Add(new AuditLog { UserId = 1, Action = "CREATE", TableName = "BomStructures", RecordId = 3,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetByTable("Materials", DateTime.UtcNow.AddHours(-2)).ToList();
        results.Should().HaveCount(2);
    }

    [Fact]
    public void GetByUser_ShouldReturnUserOperations()
    {
        _repo.Add(new AuditLog { UserId = 42, Action = "UPDATE", TableName = "Prices", RecordId = 1,
            Timestamp = DateTime.UtcNow });
        _repo.Add(new AuditLog { UserId = 99, Action = "DELETE", TableName = "Prices", RecordId = 2,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetByUser(42, 10).ToList();
        results.Should().HaveCount(1);
        results[0].Action.Should().Be("UPDATE");
    }

    [Fact]
    public void GetRecent_ShouldReturnLatestFirst()
    {
        var uniqueTable = "RecentTest_" + Guid.NewGuid().ToString("N")[..8];
        _repo.Add(new AuditLog { UserId = 1, Action = "A", TableName = uniqueTable, RecordId = 1,
            Timestamp = DateTime.UtcNow.AddMinutes(-5) });
        _repo.Add(new AuditLog { UserId = 1, Action = "B", TableName = uniqueTable, RecordId = 2,
            Timestamp = DateTime.UtcNow });

        var results = _repo.GetRecent(100).ToList();
        var myEntries = results.Where(r => r.TableName == uniqueTable).ToList();
        myEntries.Should().HaveCount(2);
        myEntries[0].Action.Should().Be("B"); // 最新在前
    }
}
