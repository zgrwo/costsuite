# 线程反模式与正确写法

> 适用于 Excel-DNA 插件开发 (COM Interop + WPF + Task.Run)

## 反模式 1: Task.Run 中直接调用 Excel COM

```csharp
// ❌ 错误: 后台线程调用 COM → RPC_E_WRONG_THREAD
await Task.Run(() =>
{
    var range = ((Application)ExcelDnaUtil.Application).ActiveCell;
    range.Value = "hello";
});
```

```csharp
// ✅ 正确: 通过 ExcelThreadDispatcher 封送回主线程
await Task.Run(() =>
{
    var result = _dispatcher.RunOnExcelThread(() =>
    {
        var range = ((Application)ExcelDnaUtil.Application).ActiveCell;
        return range.Value?.ToString();
    });
});
```

## 反模式 2: WPF Timer 回调中更新 Excel

```csharp
// ❌ 错误: DispatcherTimer 回调在 WPF 线程，非 Excel 主线程
_timer.Tick += (s, e) => { ExcelAsyncUtil.QueueAsMacro(() => { /* COM */ }); };
```

```csharp
// ✅ 正确: 始终使用 QueueAsMacro 或 ExcelThreadDispatcher
_timer.Tick += (s, e) =>
{
    _dispatcher.RunOnExcelThread<object?>(() => { /* COM 操作 */ return null; });
};
```

## 反模式 3: UDF 标记 IsThreadSafe=true 但访问非线程安全资源

```csharp
// ❌ 错误: 标记线程安全但使用共享 SQLite 连接
[ExcelFunction(IsThreadSafe = true)]
public static object MyFunc(string code)
{
    return _sharedConnection.Query(...); // 并发访问 → 数据损坏
}
```

```csharp
// ✅ 正确: IsThreadSafe=false（当前方案）或每次创建独立连接
[ExcelFunction(IsThreadSafe = false)]
public static object MyFunc(string code)
{
    using var scope = Container.BeginScope();
    var repo = scope.ServiceProvider.GetRequiredService<IMaterialRepository>();
    return repo.GetByCode(code);
}
```

## 反模式 4: async void 事件处理器中丢失异常

```csharp
// ❌ 错误: 异常被吞没
button.Click += async (s, e) => { await DoWork(); };
```

```csharp
// ✅ 正确: try-catch 包裹 + 日志
button.Click += async (s, e) =>
{
    try { await DoWork(); }
    catch (Exception ex) { AppLogger.Error($"操作失败: {ex}", ex, GetType()); }
};
```

## 核心原则

1. **Excel COM 只在主线程** — 任何跨线程 COM 调用必须经 `ExcelThreadDispatcher`
2. **后台线程只做 IO/计算** — SQLite、DuckDB、纯算法
3. **WPF 更新走 Dispatcher** — `_uiDispatcher.Invoke()` 或 `CheckAccess()`
4. **UDF 默认 IsThreadSafe=false** — 除非证明无共享状态
