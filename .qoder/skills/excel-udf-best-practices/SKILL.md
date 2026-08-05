---
description: "Excel UDF 设计最佳实践 — [ExcelFunction] 参数/返回值规范、错误处理、ExplicitExports。"
name: "Excel UDF 设计最佳实践"
---

# Skill: Excel UDF 设计最佳实践

> **TRIGGER**: 修改 `src/BomAddIn/UDF/` 下任何 `.cs` 文件时，或编写/修改 `[ExcelFunction]` 方法时，**必须**先读此 Skill。
>
> **来源**: [Collection-of-CSharp-ExcelDNA-UDFs](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs)、[DBAddin](https://github.com/rkapl123/DBAddin)、[xlDuckDb](https://github.com/RusselWebber/xlDuckDb)  
> **适用范围**: 所有 `[ExcelFunction]` 的设计、签名、错误处理和性能优化

---

## 1. UDF 签名设计法则

### 1.1 四条黄金规则

| 规则 | 说明 | 示例 |
|------|------|------|
| **1. 名称即文档** | 函数名自解释，无需额外注释 | ✅ `BOMEXPAND` ❌ `UDF001` |
| **2. 输入宽容，输出严格** | 接受多种输入格式，输出固定格式 | 日期接受 string/double/DateTime |
| **3. 可选参数有默认值** | 所有非核心参数都应是可选的 | `[date], [versionState]` |
| **4. 一个函数一件事** | 不要做瑞士军刀 UDF | `BOMEXPAND` 不兼做成本汇总 |

### 1.2 函数命名规范

```text
动词 + 名词 + [修饰语]

✅ BOMEXPAND       — 展开 BOM
✅ BOMCOST         — 计算 BOM 成本
✅ PRICELOOKUP     — 查询价格
✅ INVENTORYQTY    — 查询库存量
✅ VARIANCECHECK   — 差异比对
✅ ALERTCHECK      — 预警检查
✅ ORDERSTATUS     — 订单状态
✅ SYNCSTATUS      — 同步状态
```

### 1.3 参数设计

```csharp
// ✅ 好的参数设计
[ExcelFunction(Name = "BOMEXPAND", Description = "展开指定物料的完整BOM结构")]
public static object[,] BomExpand(
    [ExcelArgument("物料编码")] string itemCode,       // 必填
    [ExcelArgument("截止日期（默认今天）")] object asOfDate = null,  // 可选，接受多种类型
    [ExcelArgument("版本状态: Draft/Released/All（默认Released）")] string versionState = "Released"
)
{
    // 参数标准化（宽容输入）
    DateTime? date = ParseDateArg(asOfDate) ?? DateTime.Today;
    var state = ParseVersionState(versionState);

    // 委托给 BLL（每次 UDF 调用创建独立 scope，确保线程安全）
    using var scope = Container.BeginScope();
    var service = scope.ServiceProvider.GetRequiredService<IBomService>();
    return service.Expand(itemCode, date, state).ToArray2D();
}

// 辅助方法：参数标准化
private static DateTime? ParseDateArg(object arg)
{
    if (arg == null || arg is ExcelMissing) return null;
    if (arg is DateTime dt) return dt;
    if (arg is double d) return DateTime.FromOADate(d);  // Excel 日期序列号
    if (DateTime.TryParse(arg.ToString(), out var parsed)) return parsed;
    throw new ArgumentException($"无法将 '{arg}' 解析为日期");
}
```

## 2. Excel 2016 vs 365：数组公式适配

```csharp
public static class ArrayFormulaHelper
{
    private static readonly IVersionAdapter _adapter;

    static ArrayFormulaHelper()
    {
        // 通过 BeginScope 获取（Singleton 服务也可直接从 ServiceProvider 解析）
        _adapter = BomAddInStartup.ServiceProvider.GetRequiredService<IVersionAdapter>();
    }

    /// <summary>
    /// 根据 Excel 版本返回合适的数组公式输出
    /// </summary>
    public static object GetArrayResult(object[,] data)
    {
        if (_adapter.IsDynamicArraySupported)
        {
            // Excel 365: 动态数组，自动溢出
            return data;
        }
        else
        {
            // Excel 2016/2019: 需要用户按 Ctrl+Shift+Enter
            // 返回固定大小区域 — 如果数据超出，在第一行末尾加提示
            const int MAX_ROWS_2016 = 1000; // 保守上限

            if (data.GetLength(0) > MAX_ROWS_2016)
            {
                var truncated = new object[MAX_ROWS_2016 + 1, data.GetLength(1)];
                Array.Copy(data, truncated, MAX_ROWS_2016 * data.GetLength(1));
                // 提示行
                truncated[MAX_ROWS_2016, 0] = $"⚠️ 数据已截断（仅显示前 {MAX_ROWS_2016} 行）。请升级 Excel 365 以获取完整结果。";
                return truncated;
            }

            return data;
        }
    }
}
```

## 3. 错误处理：Excel 友好的方式

```csharp
// ❌ 错误做法：让异常传播到 Excel（显示难懂的错误对话框）
[ExcelFunction]
public static object BadUdf(string input)
{
    return int.Parse(input); // 抛 FormatException → Excel 显示混乱错误
}

// ✅ 正确做法：返回 Excel 错误值，并在任务窗格中提示
[ExcelFunction(Name = "PRICELOOKUP")]
public static object PriceLookup(string itemCode, string supplierCode)
{
    try
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            return ExcelError.ExcelErrorNA;  // #N/A → "物料编码为空"

        var service = Container.ResolveWithScope<IPriceService>();
        var price = service.Service.GetPrice(itemCode, supplierCode);

        if (price == null)
            return ExcelError.ExcelErrorNA;  // #N/A → "未找到价格"

        return price.Value;
    }
    catch (Exception ex)
    {
        // 记录完整错误（含栈追踪）
        LogManager.GetCurrentClassLogger().Error(ex,
            "PRICELOOKUP 错误: itemCode={itemCode}, supplierCode={supplierCode}",
            itemCode, supplierCode);

        // 用户看到 #VALUE! — Excel 的标准错误处理
        return ExcelError.ExcelErrorValue;
    }
}
```

**Excel 错误值语义**:

| 返回值 | Excel 显示 | 含义 | 使用场景 |
|--------|-----------|------|---------|
| `ExcelError.ExcelErrorNA` | `#N/A` | 找不到 | 物料不存在、价格无数据 |
| `ExcelError.ExcelErrorValue` | `#VALUE!` | 参数错误 | 参数类型/范围不合法 |
| `ExcelError.ExcelErrorRef` | `#REF!` | 引用无效 | 引用的 BOM 版本已删除 |
| `ExcelError.ExcelErrorNum` | `#NUM!` | 数值超限 | BOM 展开超过最大层级 |
| `ExcelError.ExcelErrorNull` | `#NULL!` | 交集为空 | 传入的范围无交集 |

## 4. 易变函数（Volatile）的管理

```csharp
// ⚠️ 易变函数：每次 Excel 重算都会执行
// 尽可能少用 Volatile，会严重拖慢工作表性能

// ✅ 非易变：结果仅取决于输入参数（推荐）
[ExcelFunction(Name = "BOMCOST", IsVolatile = false)]
public static double BomCost(string itemCode, DateTime? asOfDate) { ... }

// ⚠️ 必须易变：结果取决于外部状态（谨慎使用）
[ExcelFunction(Name = "SYNCSTATUS", IsVolatile = true)]
public static string SyncStatus()
{
    // 每次重算都会调用 — 但开销小（读一个状态字段）
    return Container.ResolveWithScope<ISyncService>().Service.GetCurrentStatus();
}
```

**替代 Volatile 的方案**:
- 使用 `ExcelAsyncUtil.Run` 触发异步刷新
- 使用 RTD (`ExcelRtdServer`) 推送变更（参考 FinAnSu）
- 在 Ribbon 按钮中触发手动刷新，而非在 UDF 中

## 5. 性能优化清单

### 5.1 宏观优化

```csharp
// ✅ 缓存数据库查询结果
[ExcelFunction(Name = "PRICELOOKUP", IsThreadSafe = true)]
public static object PriceLookup(string itemCode, string supplierCode)
{
    var cache = Container.ResolveWithScope<ICacheProvider>();
    var key = $"PRICELOOKUP:{itemCode}:{supplierCode}";

    return cache.Service.GetOrSet(key, TimeSpan.FromMinutes(5), () =>
    {
        using var scope = Container.BeginScope();
        var service = scope.ServiceProvider.GetRequiredService<IPriceService>();
        return service.GetPrice(itemCode, supplierCode);
    });
}
```

```csharp
// ✅ 同工作表多次调用去重
// 在一次计算链中，同一 sheet 中 =BOMEXPAND("A") 和另一个 =BOMEXPAND("A")
// 应该共享结果 （Excel 自己的计算引擎对此也有优化，但显式 L0 缓存更可靠）
```

### 5.2 微观优化

| 优化 | 说明 |
|------|------|
| `IsThreadSafe = true` | 允许 Excel 并行计算多个 UDF 实例 |
| `ExcelReference` 替代 `Range` | 轻量句柄，避免 COM 封送开销 |
| 预分配数组大小 | `new object[rows, cols]` 而非动态 `List` |
| 避免 UDF 中写日志 | 高频 UDF 中 NLog 开销大；用 `Debug.WriteLine` 或采样日志 |

## 6. UDF 开发与调试技巧

```csharp
// 1. 在 Excel 中快速测试 UDF
//    =BOMEXPAND("MAT-001") → 在公式栏 F9 求值 → 观察结果

// 2. 附加调试器
#if DEBUG
[ExcelFunction(Name = "DEBUG_ATTACH")]
public static string DebugAttach()
{
    Debugger.Launch();
    return "Debugger attached";
}
#endif

// 3. 打印诊断信息
[ExcelFunction(Name = "DIAGINFO")]
public static string DiagInfo()
{
    var sb = new StringBuilder();
    sb.AppendLine($"Excel Version: {Application.Version}");
    sb.AppendLine($"Dynamic Arrays: {VersionAdapter.IsDynamicArraySupported}");
    sb.AppendLine($"Is 64-bit: {Environment.Is64BitProcess}");
    sb.AppendLine($"DB Connected: {HealthCheck.IsDatabaseConnected()}");
    sb.AppendLine($"Offline Mode: {NetworkMonitor.IsOffline}");
    return sb.ToString();
}
```

## 7. 自检清单

- [ ] 函数名全大写、无空格、自解释（`BOMEXPAND` 不是 `bom_expand`）
- [ ] 所有可选参数有合理默认值
- [ ] 参数接受多种输入格式（`DateTime`、`double` (OADate)、`string`）
- [ ] 错误返回 `ExcelError.*` 而非抛异常
- [ ] 高频 UDF 有缓存（MemoryCache L1）
- [ ] 尽可能标记 `IsThreadSafe = true`
- [ ] 尽可能标记 `IsVolatile = false`
- [ ] 数组 UDF 处理了 Excel 2016（固定区域）和 Excel 365（动态溢出）两种路径
- [ ] 参数为空/无效时返回 `#N/A` 或 `#VALUE!`，而非静默返回空

## 8. 参考

- [Collection-of-CSharp-ExcelDNA-UDFs: 线程安全 UDF 集合](https://github.com/ngpepin/Collection-of-CSharp-ExcelDNA-UDFs)
- [DBAddin: 数据库查询 UDF 封装模式](https://github.com/rkapl123/DBAddin)
- [xlDuckDb: 嵌入式分析引擎 UDF](https://github.com/RusselWebber/xlDuckDb)
- [Excel-DNA: UDF 开发文档](https://excel-dna.net/docs/guides-advanced/performing-asynchronous-work/)
