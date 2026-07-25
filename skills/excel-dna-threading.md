# Skill: Excel-DNA 线程安全最佳实践

> **TRIGGER**: 修改 `src/BomAddIn/Bridge/` 或任何涉及 `ExcelThreadDispatcher`、`ExcelAsyncUtil.QueueAsMacro`、COM 交互的代码时，**必须**先读此 Skill。
>
> **来源**: [Excel-DNA DeepWiki](https://deepwiki.com/Excel-DNA/ExcelDna/)、[DotNetRefEdit](https://github.com/Ron-Ldn/DotNetRefEdit)、[Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs)  
> **适用范围**: 所有需要与 Excel COM 交互的代码  
> **严重级别**: 🔴 违反将导致 `RPC_E_WRONG_THREAD` 异常或随机崩溃

---

## 1. 核心原则

```
┌─────────────────────────────────────────────────────┐
│  唯一规则：绝不从非 Excel 主线程触碰 COM 对象       │
│                                                     │
│  ❌ Task.Run(() => range.Value = "data")            │
│  ❌ WPF Button_Click 中直接写 cell                  │
│  ❌ Timer 回调中调用 Application.Range[...]         │
│  ❌ async UDF 的 await 之后访问 COM                 │
│                                                     │
│  ✅ 全部通过 ExcelThreadDispatcher 封送             │
└─────────────────────────────────────────────────────┘
```

## 2. 决策树：我的代码该用哪种线程策略？

```text
Q: 这段代码是否需要访问 Excel COM 对象？
│
├─ 否 → 纯计算（数学、LINQ、JSON 解析）
│      → 标记 [ExcelFunction(IsThreadSafe = true)]
│      → 可在 ThreadPool 上并行执行
│
└─ 是 → Q: 当前线程是 Excel 主线程吗？
       │
       ├─ 是 → 直接调用（已在安全上下文）
       │
       └─ 否 → Q: 这是 UDF 还是 UI 代码？
              │
              ├─ UDF → 使用 Click-to-Run 模式
              │        return ExcelAsyncUtil.Run("label", params, () => { ... });
              │
              └─ UI (WPF/Ribbon) → 使用 QueueAsMacro 封送
                       ExcelThreadDispatcher.RunOnExcelThreadAsync(() => { ... });
```

## 3. 代码范式

### 3.1 ExcelThreadDispatcher（基础）

```csharp
public static class ExcelThreadDispatcher
{
    private static int _excelThreadId = -1;

    // AutoOpen 时调用一次
    public static void CaptureExcelThread()
    {
        _excelThreadId = Environment.CurrentManagedThreadId;
    }

    public static bool IsExcelMainThread =>
        Environment.CurrentManagedThreadId == _excelThreadId;

    // 同步封送 — 调用方不在主线程时使用
    // ⚠️ 不得在 Excel 主线程上以同步方式调用此方法（会死锁）
    public static T RunOnExcelThread<T>(Func<T> action)
    {
        if (IsExcelMainThread)
            return action();

        // QueueAsMacro 将委托放入 Excel 消息队列，等待主线程执行
        return ExcelAsyncUtil.QueueAsMacro(() => action());
    }

    // 异步封送 — WPF / 后台线程的首选
    public static async Task<T> RunOnExcelThreadAsync<T>(Func<T> action)
    {
        if (IsExcelMainThread)
            return action();

        var tcs = new TaskCompletionSource<T>();
        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    // 无返回值的便捷方法
    public static void RunOnExcelThread(Action action)
    {
        RunOnExcelThread(() => { action(); return true; });
    }
}
```

### 3.2 IsThreadSafe = true 的正确用法

```csharp
// ✅ 正确：纯计算，无状态，无 COM
[ExcelFunction(Name = "BOMCOST", IsThreadSafe = true)]
public static double BomCost(string itemCode, DateTime? asOfDate)
{
    // 通过 DI 获取服务（服务本身必须是线程安全的）
    var service = Container.Resolve<IBomService>();
    return service.CalculateCost(itemCode, asOfDate ?? DateTime.Today);
}

// ❌ 错误：标记了 IsThreadSafe 但内部调用了 COM
[ExcelFunction(IsThreadSafe = true)]  // ← 危险!
public static object DangerousUdf(string input)
{
    var app = (Application)ExcelDnaUtil.Application;  // ← COM 访问!
    return app.Range["A1"].Value;
}
```

### 3.3 异步 UDF：Click-to-Run 模式

当 UDF 需要访问 COM 但计算耗时较长时（如数据库查询）：

```csharp
[ExcelFunction(Name = "PRICELOOKUP")]
public static object PriceLookup(string itemCode, string supplierCode)
{
    // ExcelAsyncUtil.Run 在后台线程执行，通过 Excel 的 RTD 机制回写结果
    return ExcelAsyncUtil.Run(
        "PriceLookup",              // 唯一标签
        new object[] { itemCode, supplierCode },
        () =>
        {
            // 这里是后台线程 — 安全做 IO、DB 查询
            var service = Container.Resolve<IPriceService>();
            var result = service.GetPrice(itemCode, supplierCode);

            // 返回简单值（不要在这里返回 COM Range）
            return (object)result;
        });
}
```

### 3.4 异步 UDF 的 SynchronizationContext 陷阱

```csharp
// ❌ 常见错误：await 之后丢失 UI 上下文
[ExcelFunction]
public static async Task<object> AsyncUdfBad(string input)
{
    var data = await FetchDataAsync(input);

    // ⚠️ SynchronizationContext.Current 是 null！
    // 任何 WPF 弹窗或 COM 访问都会失败
    ShowErrorDialog("Failed");  // ← 抛出 STA 异常

    return data;
}

// ✅ 正确：在 AutoOpen 中捕获 SynchronizationContext
private static SynchronizationContext _uiContext;

public static void AutoOpen()
{
    _uiContext = new DispatcherSynchronizationContext(
        Dispatcher.CurrentDispatcher);
}

[ExcelFunction]
public static async Task<object> AsyncUdfGood(string input)
{
    try
    {
        return await FetchDataAsync(input);
    }
    catch (Exception ex)
    {
        // 手动 Post 回 UI 线程
        _uiContext.Post(_ => ShowErrorDialog(ex.Message), null);
        return ExcelError.ExcelErrorValue;  // 返回 #VALUE!
    }
}
```

## 4. 常见反模式与修复

| 反模式 | 症状 | 修复 |
|--------|------|------|
| WPF 按钮事件直接写 cell | `RPC_E_WRONG_THREAD` | 使用 `RunOnExcelThreadAsync(() => range.Value = data)` |
| `Timer.Elapsed` 中访问 Excel | 随机崩溃 | 在 Timer 回调中仅设置标志，用 Excel 的 `Application.OnTime` 轮询 |
| 在 `Marshal.ReleaseComObject` 后使用对象 | `COMException: 对象已断开` | 不使用 `ReleaseComObject`，让 GC 处理 |
| UDF 中缓存 `Application` 引用 | Excel 关闭时残留引用 | 每次从 `ExcelDnaUtil.Application` 获取，不缓存 |
| `Parallel.ForEach` 中调用 COM | 多个线程同时打 Excel | 收集结果后在主线程一次性写入 |

## 5. 自检清单

在提交代码前，确认以下每一项：

- [ ] 所有 `[ExcelFunction]` 要么标记 `IsThreadSafe = true`（且确实不碰 COM），要么使用 `ExcelAsyncUtil.Run`
- [ ] WPF/Ribbon 事件处理器中所有 Excel 写入都通过 `RunOnExcelThreadAsync`
- [ ] `AutoOpen()` 中已调用 `ExcelThreadDispatcher.CaptureExcelThread()`
- [ ] 异步 UDF 的 catch 块使用 `_uiContext.Post` 而非直接弹窗
- [ ] 无任何 `Task.Run(() => { /* 访问 COM */ })` 的代码
- [ ] 无任何 `Marshal.ReleaseComObject` 调用
- [ ] 无静态字段缓存 `Application` 或 `Range` 对象

## 6. 参考

- [Excel-DNA: Development Best Practices (DeepWiki)](https://deepwiki.com/Excel-DNA/ExcelDna/6.3-development-best-practices)
- [Excel-DNA: Performing Asynchronous Work](https://excel-dna.net/docs/guides-advanced/performing-asynchronous-work/)
- [DotNetRefEdit: WPF + Excel Threading](https://github.com/Ron-Ldn/DotNetRefEdit)
- [Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs)
