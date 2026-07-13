using System;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class PriceRecordRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly PriceRecordRepository _repo;
    private static int _counter;

    public PriceRecordRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new PriceRecordRepository(fixture);
    }

    private long CreateMaterial()
    {
        var matRepo = new MaterialRepository(_fixture);
        var code = $"PM-{System.Threading.Interlocked.Increment(ref _counter):D4}";
        var m = new Material { OrgId = 1, Code = code, Name = "PTest" };
        matRepo.Add(m);
        return m.Id;
    }

    [Fact]
    public void GetLatestByMaterialId_ShouldReturnMostRecent()
    {
        var matId = CreateMaterial();
        _repo.BulkUpsert(new[]
        {
            new PriceRecord { OrgId = 1, MaterialId = matId, SupplierId = 1,
                UnitPrice = 10.5m, Currency = "CNY", DataVersion = 1, EffectiveDate = DateTime.Today.AddDays(-30) },
            new PriceRecord { OrgId = 1, MaterialId = matId, SupplierId = 1,
                UnitPrice = 15.0m, Currency = "CNY", DataVersion = 3, EffectiveDate = DateTime.Today }
        });

        var latest = _repo.GetLatestByMaterialId(matId);
        latest.Should().NotBeNull();
        latest!.UnitPrice.Should().Be(15.0m);
    }

    [Fact]
    public void GetByMaterialVersion_ShouldFilterByVersion()
    {
        var matId = CreateMaterial();
        _repo.BulkUpsert(new[]
        {
            new PriceRecord { OrgId = 1, MaterialId = matId, SupplierId = 1,
                UnitPrice = 100m, Currency = "CNY", DataVersion = 5, EffectiveDate = DateTime.Today }
        });

        var results = _repo.GetByMaterialVersion(matId, 5).ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void GetHistory_ShouldReturnDateRange()
    {
        var matId = CreateMaterial();
        _repo.BulkUpsert(new[]
        {
            new PriceRecord { OrgId = 1, MaterialId = matId, SupplierId = 1,
                UnitPrice = 50m, Currency = "USD", DataVersion = 10, EffectiveDate = new DateTime(2026, 6, 15) }
        });

        var results = _repo.GetHistory(matId, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void BulkUpsert_ShouldReplaceExisting()
    {
        var matId = CreateMaterial();
        var record = new PriceRecord
        {
            OrgId = 1, MaterialId = matId, SupplierId = 2,
            UnitPrice = 22.5m, Currency = "CNY", DataVersion = 100,
            EffectiveDate = DateTime.Today
        };
        _repo.BulkUpsert(new[] { record });

        record.UnitPrice = 25.0m;
        _repo.BulkUpsert(new[] { record });

        var latest = _repo.GetLatestByMaterialId(matId);
        latest!.UnitPrice.Should().Be(25.0m);
    }
}
