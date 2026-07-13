using BomAddIn.Core.Services;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models.Enums;
using FluentAssertions;
using Xunit;

namespace BomAddIn.IntegrationTests;

public class ConfigServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;

    public ConfigServiceTests(SqliteTestFixture fixture) => _fixture = fixture;

    private static IAuthorizationService CreateAuthz()
    {
        // 集成测试用宽松授权 stub：所有权限检查通过
        return new AllowAllAuthz();
    }

    private ConfigService CreateService(IAppConfigRepository repo, ICacheProvider cache)
    {
        return new ConfigService(repo, cache, CreateAuthz());
    }

    [Fact]
    public void GetValue_NotFound_ReturnsDefault()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        var value = service.GetValue("nonexistent", "fallback");
        value.Should().Be("fallback");
    }

    [Fact]
    public void SetValue_AndGetValue_Roundtrip()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("test_key", "test_value", "test description");

        var value = service.GetValue("test_key");
        value.Should().Be("test_value");
    }

    [Fact]
    public void SetValue_OverwritesExisting()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("overwrite_key", "v1");
        service.SetValue("overwrite_key", "v2");

        service.GetValue("overwrite_key").Should().Be("v2");
    }

    [Fact]
    public void GetValue_CacheHit_DoesNotQueryDb()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("cache_key", "cached_value");
        // 第二次获取应从缓存命中
        var value = service.GetValue("cache_key");
        value.Should().Be("cached_value");
    }

    [Fact]
    public void GetAll_ReturnsAllConfig()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("ga_1", "a");
        service.SetValue("ga_2", "b");

        var all = service.GetAll();
        all.Should().NotBeNull();
    }

    [Fact]
    public void WarmUp_LoadsAllToCache()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("wu_key", "wu_val");

        // WarmUp 后应立即命中缓存
        service.WarmUp();
        var value = service.GetValue("wu_key", "should_not_return_this");
        value.Should().Be("wu_val");
    }

    /// <summary>集成测试用的宽松授权 stub — 所有权限检查通过</summary>
    private class AllowAllAuthz : IAuthorizationService
    {
        public bool Authorize(UserRole role, BomOperation operation) => true;
        public void Demand(UserRole role, BomOperation operation) { }
        public bool IsAdmin(UserRole role) => true;
    }
}
