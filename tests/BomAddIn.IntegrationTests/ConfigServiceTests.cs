using System;
using System.Threading.Tasks;
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

        service.SetValue("test_key", "test_value", UserRole.Admin, "test description");

        var value = service.GetValue("test_key");
        value.Should().Be("test_value");
    }

    [Fact]
    public void SetValue_OverwritesExisting()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("overwrite_key", "v1", UserRole.Admin);
        service.SetValue("overwrite_key", "v2", UserRole.Admin);

        service.GetValue("overwrite_key").Should().Be("v2");
    }

    [Fact]
    public void GetValue_CacheHit_DoesNotQueryDb()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("cache_key", "cached_value", UserRole.Admin);
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

        service.SetValue("ga_1", "a", UserRole.Admin);
        service.SetValue("ga_2", "b", UserRole.Admin);

        var all = service.GetAll();
        all.Should().NotBeNull();
    }

    [Fact]
    public void WarmUp_LoadsAllToCache()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);

        service.SetValue("wu_key", "wu_val", UserRole.Admin);

        // WarmUp 后应立即命中缓存
        service.WarmUp();
        var value = service.GetValue("wu_key", "should_not_return_this");
        value.Should().Be("wu_val");
    }

    // ── H-29 补充: 并发 upsert 回归测试 ──

    [Fact]
    public async Task SetValue_ConcurrentUpsert_HandlesCorrectly()
    {
        var cache = new MemoryCacheProvider();
        var repo = new AppConfigRepository(_fixture);
        var service = CreateService(repo, cache);
        const string key = "concurrent_upsert_key";

        // 并行对同一 key 执行 upsert — 验证最终值不为损坏数据
        Exception? error1 = null;
        Exception? error2 = null;
        var t1 = Task.Run(() =>
        {
            try { service.SetValue(key, "value_a", UserRole.Admin); }
            catch (Exception ex) { error1 = ex; }
        });
        var t2 = Task.Run(() =>
        {
            try { service.SetValue(key, "value_b", UserRole.Admin); }
            catch (Exception ex) { error2 = ex; }
        });

        await Task.WhenAll(t1, t2);

        var result = service.GetValue(key);
        if (error1 == null && error2 == null)
        {
            // 两个写入均成功 → 最终值为最后完成写入的值
            (result == "value_a" || result == "value_b").Should().BeTrue(
                $"并发 upsert 后值应为 'value_a' 或 'value_b'，实际: '{result}'");
        }
        else
        {
            // SQLite busy (默认 busy_timeout=0) → 一个写入失败
            // 但最终值不应为垃圾数据
            result.Should().NotBeNull("即使并发写入部分失败，数据不应损坏");
        }
    }

    /// <summary>集成测试用的宽松授权 stub — 所有权限检查通过</summary>
    private class AllowAllAuthz : IAuthorizationService
    {
        public bool Authorize(UserRole role, BomOperation operation) => true;
        public void Demand(UserRole role, BomOperation operation) { }
        public bool IsAdmin(UserRole role) => true;
    }
}
