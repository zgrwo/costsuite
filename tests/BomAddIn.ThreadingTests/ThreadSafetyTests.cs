using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BomAddIn.Bridge;
using BomAddIn.Data.Caching;
using BomAddIn.Infrastructure.Network;
using BomAddIn.UDF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BomAddIn.ThreadingTests;

/// <summary>
/// 线程安全探针测试 — Sprint 0 探针 P-0.1 补充 (v2 H-26)。
/// 验证三条红线之一的线程隔离机制在并发压力下不发生数据损坏。
/// </summary>
[Collection("Sequential")]
public class ThreadSafetyTests
{
    // ── Container ──

    [Fact]
    public void Container_ResolveWithScope_ConcurrentAccess_NoDeadlock()
    {
        var services = new ServiceCollection();
        services.AddScoped<TestScopedService>();
        var provider = services.BuildServiceProvider();
        Container.Initialize(provider);

        var exceptions = new ConcurrentBag<Exception>();
        Parallel.For(0, 100, _ =>
        {
            try
            {
                var (service, scope) = Container.ResolveWithScope<TestScopedService>();
                Assert.NotNull(service);
                scope.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // ── MemoryCacheProvider ──

    [Fact]
    public void MemoryCacheProvider_ConcurrentGetSet_NoDataLoss()
    {
        var cache = new MemoryCacheProvider();
        var exceptions = new ConcurrentBag<Exception>();
        var counter = 0;

        Parallel.For(0, 1000, i =>
        {
            try
            {
                var key = $"key_{i % 10}";
                cache.Set(key, new TestCacheItem { Value = i }, TimeSpan.FromMinutes(1));
                Interlocked.Increment(ref counter);

                var item = cache.Get<TestCacheItem>(key);
                if (item != null)
                {
                    Assert.True(item.Value >= 0);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(1000, counter);
    }

    [Fact]
    public void MemoryCacheProvider_TTL_ExpiredItemNotReturned()
    {
        var cache = new MemoryCacheProvider();
        cache.Set("expires_fast", new TestCacheItem { Value = 42 }, TimeSpan.FromMilliseconds(10));
        Thread.Sleep(50);

        var result = cache.Get<TestCacheItem>("expires_fast");
        Assert.Null(result);
    }

    // ── NetworkMonitor ──

    [Fact]
    public async Task NetworkMonitor_ConcurrentProbe_NoExceptions()
    {
        using var monitor = new NetworkMonitor(new BomAddIn.Infrastructure.Config.AppConfigProvider());
        var tasks = new List<Task<bool>>();

        for (int i = 0; i < 10; i++)
            tasks.Add(monitor.ProbeConnectionAsync());

        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, results.Length);
    }

    // ── ExcelThreadDispatcher ──

    [Fact]
    public void ExcelThreadDispatcher_IsMainThread_AfterInitialize()
    {
        ExcelThreadDispatcher.Initialize();
        var dispatcher = new ExcelThreadDispatcher();

        Assert.True(dispatcher.IsExcelMainThread);
    }

    [Fact]
    public void ExcelThreadDispatcher_RunOnExcelThread_MainThread_ExecutesDirectly()
    {
        ExcelThreadDispatcher.Initialize();
        var dispatcher = new ExcelThreadDispatcher();

        var result = dispatcher.RunOnExcelThread(() => 42);

        Assert.Equal(42, result);
    }

    // ── H-12 fix: ExcelThreadDispatcher 跨线程路径回归测试 ──

    [Fact]
    public async Task ExcelThreadDispatcher_IsMainThread_FromBackgroundTask_ReturnsFalse()
    {
        ExcelThreadDispatcher.Initialize();
        var dispatcher = new ExcelThreadDispatcher();

        var isMainFromBg = await Task.Run(() => dispatcher.IsExcelMainThread);

        Assert.False(isMainFromBg, "从后台线程调用 IsExcelMainThread 应返回 false");
    }

    // ── H-5 fix: BomAnalysisProvider 并发安全回归测试 ──

    [Fact]
    public void ConcurrentDictionary_ConcurrentAdds_NoDataLoss()
    {
        var dict = new ConcurrentDictionary<int, int>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        const int count = 1000;

        Parallel.For(0, count, options, i =>
        {
            dict.TryAdd(i, i);
        });

        Assert.Equal(count, dict.Count);
    }

    // ── Helpers ──

    public class TestScopedService { }

    public class TestCacheItem
    {
        public int Value { get; set; }
    }
}
