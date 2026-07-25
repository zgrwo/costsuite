using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class MaterialRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly MaterialRepository _repo;
    // Interlocked.Increment ensures thread-safety even with xUnit parallel execution
    private static int _counter;

    public MaterialRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new MaterialRepository(fixture);
    }

    private static string NextCode() => $"MT-{System.Threading.Interlocked.Increment(ref _counter):D4}";

    [Fact]
    public void Add_ShouldInsertAndReturnId()
    {
        var material = new Material { OrgId = 1, Code = NextCode(), Name = "Test", Category = "RawMaterial" };
        _repo.Add(material);
        material.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetById_ShouldReturnInsertedMaterial()
    {
        var material = new Material { OrgId = 1, Code = NextCode(), Name = "Steel", Category = "RawMaterial" };
        _repo.Add(material);

        var retrieved = _repo.GetById(material.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Steel");
    }

    [Fact]
    public void GetByCode_ShouldReturnCorrectMaterial()
    {
        var code = NextCode();
        var material = new Material { OrgId = 1, Code = code, Name = "Bolt", Category = "Mechanical" };
        _repo.Add(material);

        var retrieved = _repo.GetByCode(1, code);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public void Search_ByCategory_ShouldFilter()
    {
        var cat = $"Cat-{NextCode()}";
        _repo.Add(new Material { OrgId = 1, Code = NextCode(), Name = "A", Category = cat });
        _repo.Add(new Material { OrgId = 1, Code = NextCode(), Name = "B", Category = cat });

        var results = _repo.Search(1, category: cat).ToList();
        results.Should().HaveCount(2);
    }

    [Fact]
    public void Update_ShouldModifyExistingMaterial()
    {
        var material = new Material { OrgId = 1, Code = NextCode(), Name = "Original" };
        _repo.Add(material);

        material.Name = "Updated";
        _repo.Update(material);

        _repo.GetById(material.Id)!.Name.Should().Be("Updated");
    }

    [Fact]
    public void Delete_ShouldRemoveMaterial()
    {
        var material = new Material { OrgId = 1, Code = NextCode(), Name = "ToDelete" };
        _repo.Add(material);
        _repo.Delete(material.Id);

        _repo.GetById(material.Id).Should().BeNull();
    }

    [Fact]
    public void GetAll_ShouldReturnAllMaterialsForOrg()
    {
        var orgId = 99;
        _repo.Add(new Material { OrgId = orgId, Code = NextCode(), Name = "X1" });
        _repo.Add(new Material { OrgId = orgId, Code = NextCode(), Name = "X2" });
        _repo.Add(new Material { OrgId = 1, Code = NextCode(), Name = "Y1" });

        var results = _repo.GetAll(orgId).ToList();
        results.Should().HaveCount(2);
    }
}
