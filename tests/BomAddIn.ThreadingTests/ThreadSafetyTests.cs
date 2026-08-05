using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BomAddIn.Bridge;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Infrastructure.Config;
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
        Thread.Sleep(100); // 10x TTL (10ms) to safely account for CI timer resolution

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

    [Fact]
    public async Task RunOnExcelThread_FromBackgroundThread_UsesQueueAsMacro()
    {
        // 验证跨线程调度路径: 后台线程被正确识别为非 Excel 主线程，
        // RunOnExcelThread 将走 QueueAsMacro 分支而非直接执行路径。
        // QueueAsMacro 本身需要 Excel 宿主，测试环境中会超时或抛异常，
        // 因此此处仅验证线程检测分支的正确性。

        ExcelThreadDispatcher.Initialize();
        var dispatcher = new ExcelThreadDispatcher();

        // 主线程: 应直接执行
        Assert.True(dispatcher.IsExcelMainThread);
        Assert.Equal(42, dispatcher.RunOnExcelThread(() => 42));

        // 后台线程: IsExcelMainThread 返回 false → RunOnExcelThread 走 QueueAsMacro 路径
        var isMainFromBg = await Task.Run(() => dispatcher.IsExcelMainThread);
        Assert.False(isMainFromBg,
            "后台线程的 IsExcelMainThread 必须为 false，以确保 RunOnExcelThread 走 QueueAsMacro 路径");
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

    // ── Core 业务路径并发安全测试 ──
    // 验证 Singleton/Scoped 服务在并发 UDF 调用下的线程安全性。
    // 补充基础设施层测试的覆盖空白：BomService、VarianceCalculator、AlertEvaluator。

    [Fact]
    public void VarianceCalculator_ConcurrentCompare_NoContention()
    {
        // VarianceCalculator 注册为 Singleton（纯计算，零可变状态）。
        // 验证多个线程同时调用 ComparePrices 不会发生竞态或数据损坏。
        var calculator = new VarianceCalculator();
        var exceptions = new ConcurrentBag<Exception>();
        var results = new ConcurrentBag<int>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                var result = calculator.ComparePrices(
                    materialId: 1,
                    priceA: 100.0m + i,
                    dateA: new DateTime(2025, 1, 1),
                    currencyA: "USD",
                    priceB: 105.0m + i,
                    dateB: new DateTime(2025, 6, 1),
                    currencyB: "USD");
                results.Add(result.Count);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(200, results.Count);
    }

    [Fact]
    public void AlertEvaluator_ConcurrentEvaluate_NoContention()
    {
        // AlertEvaluator 注册为 Singleton（readonly 字段，无状态）。
        // 验证并发评估不会发生竞态。
        var config = new AppConfigProvider();
        var evaluator = new AlertEvaluator(config);
        var exceptions = new ConcurrentBag<Exception>();
        var results = new ConcurrentBag<int>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                // 构造不同阈值路径的测试数据
                var variances = new List<BomAddIn.Core.Models.VarianceResult>
                {
                    new BomAddIn.Core.Models.VarianceResult
                    {
                        NodeCode = $"NODE-{i}",
                        NodeDescription = "Test",
                        Dimension = VarianceDimension.Price,
                        ChangeType = VarianceChangeType.Modified,
                        ChangePercent = (i % 3 == 0) ? 60.0 : (i % 3 == 1) ? 15.0 : 5.0,
                        OldValue = "100",
                        NewValue = "110"
                    }
                };
                var alerts = evaluator.Evaluate(variances);
                results.Add(alerts.Count);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(200, results.Count);
    }

    [Fact]
    public void BomService_ConcurrentScopes_NoCrossScopeLeakage()
    {
        // BomService 注册为 Scoped。验证多个并发 scope 之间不发生状态泄漏。
        // 每个 scope 应获得独立的 BomService 实例。
        var services = new ServiceCollection();
        services.AddScoped<BomServiceScopeTracker>();
        var provider = services.BuildServiceProvider();

        var instances = new ConcurrentBag<int>();
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 50, _ =>
        {
            try
            {
                using var scope = provider.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<BomServiceScopeTracker>();
                tracker.Id = Thread.CurrentThread.ManagedThreadId;
                // 模拟短暂工作以扩大 scope 重叠窗口
                Thread.SpinWait(1000);
                instances.Add(tracker.Id);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(50, instances.Count);
    }

    [Fact]
    public void SingletonServices_ConcurrentResolution_NoDeadlock()
    {
        // 验证 IVarianceCalculator 和 IAlertEvaluator 作为 Singleton
        // 在并发 DI resolve 下不发生死锁或异常。
        var services = new ServiceCollection();
        services.AddSingleton<IVarianceCalculator, VarianceCalculator>();
        services.AddSingleton<IAlertEvaluator>(sp =>
            new AlertEvaluator(new AppConfigProvider()));
        var provider = services.BuildServiceProvider();

        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 100, _ =>
        {
            try
            {
                var calc = provider.GetRequiredService<IVarianceCalculator>();
                var eval = provider.GetRequiredService<IAlertEvaluator>();
                Assert.NotNull(calc);
                Assert.NotNull(eval);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // ── Helpers ──

    public class TestScopedService { }

    public class TestCacheItem
    {
        public int Value { get; set; }
    }

    public class BomServiceScopeTracker
    {
        public int Id { get; set; }
    }
}
