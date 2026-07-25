using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BomAddIn.Bridge;
using BomAddIn.Data.Caching;
using BomAddIn.UDF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace BomAddIn.ThreadingTests;

/// <summary>
/// 线程压力测试 — Phase 3 验收标准：30min 连续运行 0 异常。
/// 
/// 默认运行 30 秒（CI 友好）。设置环境变量 BOM_STRESS_MINUTES=30 可运行完整 30 分钟。
/// 
/// 模拟真实负载：
/// - 并发 DI Scope 创建/解析/释放
/// - 并发缓存读写（含 TTL 过期）
/// - 并发线程检测（ExcelThreadDispatcher）
/// - 并发 Dictionary/List 操作（模拟 BOM 展开的 HashSet 去重）
/// </summary>
[Collection("Sequential")]
public class ThreadStressTests
{
    private readonly ITestOutputHelper _output;

    public ThreadStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>获取压力测试持续时间</summary>
    private static TimeSpan GetStressDuration()
    {
        var envMinutes = Environment.GetEnvironmentVariable("BOM_STRESS_MINUTES");
        if (int.TryParse(envMinutes, out var minutes) && minutes > 0)
            return TimeSpan.FromMinutes(minutes);

        // CI 默认 30 秒
        return TimeSpan.FromSeconds(30);
    }

    [Fact]
    public async Task Stress_ConcurrentMixedOperations_ZeroExceptions()
    {
        var duration = GetStressDuration();
        _output.WriteLine($"压力测试持续时间: {duration.TotalSeconds:F0}s");

        // 初始化
        ExcelThreadDispatcher.Initialize();
        var dispatcher = new ExcelThreadDispatcher();
        var cache = new MemoryCacheProvider();
        var services = new ServiceCollection();
        services.AddScoped<ScopedDummy>();
        services.AddSingleton<SingletonDummy>();
        var provider = services.BuildServiceProvider();
        Container.Initialize(provider);

        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource(duration);
        var token = cts.Token;

        long totalOps = 0;
        var sw = Stopwatch.StartNew();

        // 启动多个并发工作者，每个模拟不同子系统
        var workers = new List<Task>();

        // Worker 1: DI Scope 创建/解析/释放
        workers.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var (svc, scope) = Container.ResolveWithScope<ScopedDummy>();
                    if (svc == null) throw new InvalidOperationException("ScopedDummy 解析失败");
                    scope.Dispose();
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // Worker 2: 缓存读写 + TTL
        workers.Add(Task.Run(() =>
        {
            var rng = new Random(42);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var key = $"stress_{rng.Next(100)}";
                    cache.Set(key, new CacheItem { Value = rng.Next() }, TimeSpan.FromMilliseconds(rng.Next(10, 500)));
                    var item = cache.Get<CacheItem>(key);
                    // item 可能为 null（TTL 过期），这是正常的
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // Worker 3: 线程检测
        workers.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 后台线程应始终返回 false
                    if (dispatcher.IsExcelMainThread)
                        throw new InvalidOperationException("后台线程不应被识别为 Excel 主线程");
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // Worker 4: HashSet 去重（模拟 BOM 展开）
        workers.Add(Task.Run(() =>
        {
            var rng = new Random(123);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var visited = new HashSet<long>();
                    for (int i = 0; i < 100; i++)
                    {
                        visited.Add(rng.Next(1000));
                    }
                    if (visited.Count > 1000)
                        throw new InvalidOperationException("HashSet 数据损坏");
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // Worker 5: 并发 Singleton 解析
        workers.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var svc = provider.GetRequiredService<SingletonDummy>();
                    if (svc == null) throw new InvalidOperationException("SingletonDummy 解析失败");
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // Worker 6: 并发 BeginScope + 多服务解析
        workers.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var scope = Container.BeginScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ScopedDummy>();
                    if (svc == null) throw new InvalidOperationException("BeginScope 解析失败");
                    Interlocked.Increment(ref totalOps);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        }));

        // 等待所有工作者完成
        await Task.WhenAll(workers.ToArray());
        sw.Stop();

        _output.WriteLine($"完成操作数: {totalOps:N0}");
        _output.WriteLine($"实际耗时: {sw.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"吞吐量: {totalOps / sw.Elapsed.TotalSeconds:N0} ops/s");
        _output.WriteLine($"异常数: {exceptions.Count}");

        if (!exceptions.IsEmpty)
        {
            // 输出前 5 个异常详情
            int count = 0;
            foreach (var ex in exceptions)
            {
                _output.WriteLine($"  [{++count}] {ex.GetType().Name}: {ex.Message}");
                if (count >= 5) break;
            }
        }

        exceptions.TryPeek(out var firstEx);
        Assert.True(exceptions.IsEmpty,
            $"压力测试发现 {exceptions.Count} 个异常。第一个: {firstEx?.Message ?? "N/A"}");
        Assert.True(totalOps > 0, "压力测试应至少完成一次操作");
    }

    [Fact]
    public async Task Stress_ConcurrentCacheHammer_NoDataCorruption()
    {
        var duration = GetStressDuration();
        var cache = new MemoryCacheProvider();
        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource(duration);
        var token = cts.Token;

        long writes = 0, reads = 0;

        var tasks = new Task[8];
        for (int t = 0; t < tasks.Length; t++)
        {
            var taskId = t;
            tasks[t] = Task.Run(() =>
            {
                var rng = new Random(taskId * 1000);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var key = $"k_{rng.Next(50)}";
                        if (rng.Next(2) == 0)
                        {
                            cache.Set(key, new CacheItem { Value = taskId }, TimeSpan.FromSeconds(5));
                            Interlocked.Increment(ref writes);
                        }
                        else
                        {
                            var item = cache.Get<CacheItem>(key);
                            if (item != null && (item.Value < 0 || item.Value >= tasks.Length))
                                throw new InvalidOperationException($"缓存数据损坏: Value={item.Value}");
                            Interlocked.Increment(ref reads);
                        }
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            });
        }

        await Task.WhenAll(tasks);

        _output.WriteLine($"缓存压力: writes={writes:N0}, reads={reads:N0}, 异常={exceptions.Count}");
        Assert.True(exceptions.IsEmpty, $"缓存压力测试发现 {exceptions.Count} 个异常");
    }

    // ── Helpers ──

    public class ScopedDummy { }
    public class SingletonDummy { }
    public class CacheItem { public int Value { get; set; } }
}
