using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Core.Services;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class BomServiceTests
{
    private readonly Mock<IBomNodeRepository> _nodeRepoMock = new();
    private readonly Mock<IBomVersionRepository> _versionRepoMock = new();
    private readonly Mock<IMaterialRepository> _materialRepoMock = new();
    private readonly Mock<IBomAnalysisProvider> _analysisMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<ICacheProvider> _cacheMock = new();
    private readonly Mock<IDbConnectionFactory> _connFactoryMock = new();
    private readonly Mock<IDbConnection> _connMock = new();
    private readonly Mock<IDbTransaction> _txMock = new();
    private readonly Mock<IAuthorizationService> _authzMock = new();
    private readonly Mock<IPriceRecordRepository> _priceRepoMock = new();
    private readonly BomService _service;

    public BomServiceTests()
    {
        _connFactoryMock.Setup(f => f.CreateConnection()).Returns(_connMock.Object);
        _connMock.Setup(c => c.BeginTransaction()).Returns(_txMock.Object);
        _service = new BomService(_nodeRepoMock.Object, _versionRepoMock.Object,
            _materialRepoMock.Object, _analysisMock.Object, _auditMock.Object, _cacheMock.Object,
            _priceRepoMock.Object, _connFactoryMock.Object, _authzMock.Object);
    }

    [Fact]
    public void Expand_CacheHit_ReturnsFromCache()
    {
        var cached = new List<BomExpandedNode> { new() { ItemCode = "X", MaterialId = 1 } };
        _cacheMock.Setup(c => c.Get<List<BomExpandedNode>>(It.IsAny<string>())).Returns(cached);

        var result = _service.Expand("MAT-001", DateTime.Today);

        result.Should().BeSameAs(cached);
        _analysisMock.Verify(a => a.ExpandBomViaClosure(It.IsAny<string>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public void Expand_CacheMiss_CallsDuckDbAndCaches()
    {
        var nodes = new List<BomExpandedNode> { new() { ItemCode = "Y", MaterialId = 2 } };
        _cacheMock.Setup(c => c.Get<List<BomExpandedNode>>(It.IsAny<string>())).Returns((List<BomExpandedNode>?)null);
        _analysisMock.Setup(a => a.ExpandBomViaClosure("MAT-001", It.IsAny<DateTime?>())).Returns(nodes);

        var result = _service.Expand("MAT-001");

        result.Should().BeEquivalentTo(nodes);
        _cacheMock.Verify(c => c.Set(It.IsAny<string>(), nodes, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public void AddNode_LogsAudit()
    {
        var node = new BomNode { Id = 0, OrgId = 1, Quantity = 3 };
        _service.AddNode(node, UserRole.Admin, userId: 42);

        _nodeRepoMock.Verify(r => r.Add(node, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _auditMock.Verify(a => a.Log(AuditAction.Create, "BomStructures", It.IsAny<long>(),
            null, It.IsAny<string>(), (long?)42), Times.Once);
    }

    [Fact]
    public void UpdateNode_LogsOldAndNewValues()
    {
        var oldNode = new BomNode { Id = 10, Quantity = 5 };
        var newNode = new BomNode { Id = 10, Quantity = 8 };
        _nodeRepoMock.Setup(r => r.GetById(10, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(oldNode);

        _service.UpdateNode(newNode, UserRole.Admin, userId: 1);

        _nodeRepoMock.Verify(r => r.Update(newNode, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _auditMock.Verify(a => a.Log(AuditAction.Update, "BomStructures", (long?)10,
            It.IsNotNull<string>(), It.IsNotNull<string>(), (long?)1), Times.Once);
    }

    [Fact]
    public void DeleteNode_LogsAudit()
    {
        var node = new BomNode { Id = 20, Quantity = 1 };
        _nodeRepoMock.Setup(r => r.GetById(20, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(node);

        _service.DeleteNode(20, UserRole.Admin, userId: 99);

        _nodeRepoMock.Verify(r => r.Delete(20, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _auditMock.Verify(a => a.Log(AuditAction.Delete, "BomStructures", (long?)20,
            It.IsNotNull<string>(), null, (long?)99), Times.Once);
    }

    [Fact]
    public void GetVersionHistory_ReturnsVersions()
    {
        var versions = new List<BomVersion> { new() { Id = 1 }, new() { Id = 2 } };
        _versionRepoMock.Setup(r => r.GetByBomId(5)).Returns(versions);

        var result = _service.GetVersionHistory(5);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetById_ReturnsFromRepository()
    {
        var node = new BomNode { Id = 30 };
        _nodeRepoMock.Setup(r => r.GetById(30)).Returns(node);

        var result = _service.GetById(30);
        result.Should().Be(node);
    }

    [Fact]
    public void GetChildren_ReturnsFromRepository()
    {
        var children = new List<BomNode> { new() };
        _nodeRepoMock.Setup(r => r.GetChildren(10, It.IsAny<DateTime?>())).Returns(children);

        var result = _service.GetChildren(10);
        result.Should().BeEquivalentTo(children);
    }
}
