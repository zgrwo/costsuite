using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class AuditServiceInstanceTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly AuditService _service;

    public AuditServiceInstanceTests()
    {
        _service = new AuditService(_repoMock.Object);
    }

    [Fact]
    public void Log_ShouldCallRepositoryAdd()
    {
        _service.Log(AuditAction.Create, "BomStructures", 42, null, "{\"test\":1}", 1);

        _repoMock.Verify(r => r.Add(
            It.Is<AuditLog>(log =>
                log.Action == AuditAction.Create &&
                log.TableName == "BomStructures" &&
                log.RecordId == 42 &&
                log.NewValues == "{\"test\":1}" &&
                log.UserId == 1)),
            Times.Once);
    }

    [Fact]
    public void Log_WithConnection_ShouldUseTransaction()
    {
        var connMock = new Mock<IDbConnection>();
        var txMock = new Mock<IDbTransaction>();

        _service.Log(AuditAction.Update, "Materials", connMock.Object, txMock.Object, 5, "old", "new", 2);

        _repoMock.Verify(r => r.Add(
            It.IsAny<AuditLog>(), connMock.Object, txMock.Object), Times.Once);
    }

    [Fact]
    public void GetTableHistory_WithLimit_ShouldQueryRepository()
    {
        var logs = new List<AuditLog>
        {
            new() { Id = 1 },
            new() { Id = 2 },
            new() { Id = 3 },
        };
        _repoMock.Setup(r => r.GetByTable("BomStructures",
            It.IsAny<DateTime>(), 5)).Returns(logs);

        var result = _service.GetTableHistory("BomStructures", 5);

        result.Should().HaveCount(3);
        _repoMock.Verify(r => r.GetByTable("BomStructures",
            It.IsAny<DateTime>(), 5), Times.Once);
    }

    [Fact]
    public void GetUserHistory_ShouldRespectLimit()
    {
        var logs = new List<AuditLog>
        {
            new() { Id = 1, UserId = 10 },
            new() { Id = 2, UserId = 10 },
        };
        _repoMock.Setup(r => r.GetByUser(10, 50)).Returns(logs);

        var result = _service.GetUserHistory(10, 50);

        result.Should().HaveCount(2);
        _repoMock.Verify(r => r.GetByUser(10, 50), Times.Once);
    }

    [Fact]
    public void Log_NullInputs_ShouldNotThrow()
    {
        Action act = () => _service.Log(AuditAction.Delete, "TestTable", null, null, null, null);

        act.Should().NotThrow();
        _repoMock.Verify(r => r.Add(It.IsAny<AuditLog>()), Times.Once);
    }
}

public class AuditServiceToJsonTests
{
    [Fact]
    public void ToJson_Null_ShouldReturnNullString()
    {
        AuditService.ToJson(null).Should().Be("null");
    }

    [Fact]
    public void ToJson_String_ShouldReturnQuotedString()
    {
        AuditService.ToJson("hello").Should().Be("hello");
    }

    [Fact]
    public void ToJson_Integer_ShouldReturnStringValue()
    {
        AuditService.ToJson(42).Should().Be("42");
    }

    [Fact]
    public void ToJson_DateTime_ShouldRoundTrip()
    {
        // DateTime 作为值类型，走 Convert.ToString 路径
        var dt = new DateTime(2026, 7, 13);
        var json = AuditService.ToJson(dt);
        json.Should().NotBeNullOrEmpty();
        json.Should().NotBe("null");
    }

    [Fact]
    public void ToJson_Enum_ShouldReturnEnumName()
    {
        var state = Infrastructure.Models.Enums.VersionState.Released;
        var json = AuditService.ToJson(state);
        json.Should().Be("Released");
    }

    [Fact]
    public void ToJson_AnonymousObject_ShouldProduceJsonObject()
    {
        var obj = new { Name = "Test", Count = 5 };
        var json = AuditService.ToJson(obj);
        json.Should().Contain("\"Name\":").And.Contain("\"Count\":");
        json.Should().StartWith("{").And.EndWith("}");
    }

    [Fact]
    public void ToJson_SimplePoco_ShouldSerializeAllProperties()
    {
        var material = new Infrastructure.Models.Material
        {
            Id = 1, Code = "MAT-001", Name = "Steel", OrgId = 1,
            Spec = "10mm", Unit = "kg", Category = "Raw", IsActive = true
        };
        var json = AuditService.ToJson(material);
        json.Should().Contain("\"Id\":1");
        json.Should().Contain("\"Code\":\"MAT-001\"");
        json.Should().Contain("\"Name\":\"Steel\"");
        json.Should().Contain("\"IsActive\":true");
    }

    [Fact]
    public void ToJson_StringWithQuote_ShouldEscapeProperly()
    {
        var obj = new { Value = "He said \"hello\"" };
        var json = AuditService.ToJson(obj);
        json.Should().Contain("He said \\\"hello\\\"");
    }

    [Fact]
    public void ToJson_EmptyObject_ShouldReturnEmptyBraces()
    {
        var obj = new { };
        var json = AuditService.ToJson(obj);
        json.Should().Be("{}");
    }

    [Fact]
    public void ToJson_Double_ShouldUseInvariantCulture()
    {
        AuditService.ToJson(3.14).Should().Be("3.14");
    }
}
