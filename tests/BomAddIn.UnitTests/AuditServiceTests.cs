using System;
using BomAddIn.Core.Services;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

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
        json.Should().Contain("\"IsActive\":True");
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
