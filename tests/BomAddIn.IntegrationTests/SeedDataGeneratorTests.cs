using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class SeedDataGeneratorTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly SeedDataGenerator _generator;

    public SeedDataGeneratorTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _generator = new SeedDataGenerator(fixture, new AllowAllAuthz());
    }

    [Fact]
    public void FullLifecycle_GenerateThenSkip()
    {
        // 1. 空数据库: HasSeedData = false
        _generator.HasSeedData().Should().BeFalse();

        // 2. 生成 150 物料 + 500 BOM + 2 月历史
        var result1 = _generator.Generate(UserRole.Admin, materialCount: 150, bomNodeCount: 500, historyMonths: 2);
        result1.Skipped.Should().BeFalse();
        result1.MaterialsCreated.Should().Be(150);
        result1.BomNodesCreated.Should().Be(500);

        // 3. 生成后 HasSeedData = true
        _generator.HasSeedData().Should().BeTrue();

        // 4. 再次生成跳过
        var result2 = _generator.Generate(UserRole.Admin, materialCount: 10, bomNodeCount: 50, historyMonths: 1);
        result2.Skipped.Should().BeTrue();
    }

    /// <summary>集成测试用的宽松授权 stub</summary>
    private class AllowAllAuthz : IAuthorizationService
    {
        public bool Authorize(UserRole role, BomOperation operation) => true;
        public void Demand(UserRole role, BomOperation operation) { }
        public bool IsAdmin(UserRole role) => true;
    }
}
