using System;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class BomNodeRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly BomNodeRepository _bomRepo;
    private readonly MaterialRepository _materialRepo;

    public BomNodeRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _bomRepo = new BomNodeRepository(fixture);
        _materialRepo = new MaterialRepository(fixture);
    }

    private Material CreateMaterial(string code, string name)
    {
        var mat = new Material { OrgId = 1, Code = code, Name = name };
        _materialRepo.Add(mat);
        return mat;
    }

    [Fact]
    public void Add_ShouldInsertBomNode()
    {
        var parent = CreateMaterial("B1-P", "Parent");
        var child = CreateMaterial("B1-C", "Child");

        var node = new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = child.Id,
            Quantity = 3.0, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        };
        _bomRepo.Add(node);

        node.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetChildren_ShouldReturnDirectChildren()
    {
        var parent = CreateMaterial("B2-P", "Parent");
        var childA = CreateMaterial("B2-A", "Child A");
        var childB = CreateMaterial("B2-B", "Child B");

        _bomRepo.Add(new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = childA.Id,
            Quantity = 2.0, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        });
        _bomRepo.Add(new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = childB.Id,
            Quantity = 5.0, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        });

        // 只为这个 parent 查询
        var children = _bomRepo.GetChildren(parent.Id, DateTime.Today).ToList();
        children.Should().HaveCount(2);
    }

    [Fact]
    public void GetById_ShouldReturnNode()
    {
        var parent = CreateMaterial("B3-P", "Parent");
        var child = CreateMaterial("B3-C", "Child");

        var node = new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = child.Id,
            Quantity = 4.0, Level = 2, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        };
        _bomRepo.Add(node);

        var retrieved = _bomRepo.GetById(node.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Quantity.Should().Be(4.0);
    }

    [Fact]
    public void GetByMaterialId_ShouldFindWhereMaterialIsChild()
    {
        var parent = CreateMaterial("B4-P", "Parent");
        var child = CreateMaterial("B4-C", "Child");

        _bomRepo.Add(new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = child.Id,
            Quantity = 2.0, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        });

        var usages = _bomRepo.GetByMaterialId(child.Id).ToList();
        usages.Should().HaveCount(1);
        usages[0].ParentMaterialId.Should().Be(parent.Id);
    }

    [Fact]
    public void Delete_ShouldRemoveNode()
    {
        var parent = CreateMaterial("B5-P", "Parent");
        var child = CreateMaterial("B5-C", "Child");

        var node = new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = child.Id,
            Quantity = 1.0, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today
        };
        _bomRepo.Add(node);
        _bomRepo.Delete(node.Id);

        _bomRepo.GetById(node.Id).Should().BeNull();
    }

    [Fact]
    public void VersionStateFilter_ShouldExcludeNonReleasedNodes()
    {
        var parent = CreateMaterial("B6-P", "Parent");
        var childA = CreateMaterial("B6-A", "Child A");
        var childB = CreateMaterial("B6-B", "Child B");

        _bomRepo.Add(new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = childA.Id,
            Quantity = 1, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Released,
            ValidFrom = DateTime.Today.AddDays(-30)
        });
        _bomRepo.Add(new BomNode
        {
            OrgId = 1, ParentMaterialId = parent.Id, ChildMaterialId = childB.Id,
            Quantity = 1, Level = 1, VersionState = Infrastructure.Models.Enums.VersionState.Draft,
            ValidFrom = DateTime.Today.AddDays(-30)
        });

        var children = _bomRepo.GetChildren(parent.Id, DateTime.Today).ToList();
        children.Should().HaveCount(1);
    }
}
