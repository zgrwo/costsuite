# BOM Add-in API Reference

> **日期**: 2026-07-13  
> **受众**: 开发者  
> **单源真理**: UDF 函数签名、参数约束、错误码、使用示例以本文档为准  
> **设计依据**: [specification.md §8](./specification.md#8-udf-函数清单)（函数分类和设计约束）

---

## 1. 函数索引

| # | 函数名 | 用途 | 分类 |
|---|--------|------|------|
| 1 | `BOMEXPAND` | 展开物料完整 BOM 结构 | BOM 查询 |
| 2 | `BOMCOST` | 计算物料汇总成本 | BOM 查询 |
| 3 | `PRICELOOKUP` | 查询物料供应商价格 | 数据查询 |
| 4 | `INVENTORYQTY` | 查询物料当前库存量 | 数据查询 |
| 5 | `VARIANCECHECK` | 比较两个 BOM 版本的差异 | 差异分析 |
| 6 | `ALERTCHECK` | 检查物料的预警状态 | 差异分析 |
| 7 | `ORDERSTATUS` | 查询物料订单状态 | 数据查询 |
| 8 | `SYNCSTATUS` | 获取当前数据同步状态 | 系统状态 |

---

## 2. 通用约定

### 参数类型

| Excel 输入 | C# 类型 | 说明 |
|-----------|---------|------|
| 文本字符串 | `string` | 物料编码、供应商编码等 |
| 数字 | `double` / `int` | 自动转换 |
| 日期 | `DateTime` 或 `double` (OADate) | 同时接受日期值、日期字符串（`"2026-07-12"`）和序列号 |
| 省略 | `ExcelMissing` → `null` | 可选参数不填时为 `null`，使用默认值 |

### 返回值约定

| 场景 | 返回值 | Excel 显示 |
|------|--------|-----------|
| 查询成功 | 对应类型的值或数组 | 正常值 |
| 参数为空/无效 | `ExcelError.ExcelErrorNA` | `#N/A` |
| 参数类型错误 | `ExcelError.ExcelErrorValue` | `#VALUE!` |
| 数据不存在 | `ExcelError.ExcelErrorNA` | `#N/A` |
| 超出最大层级 | `ExcelError.ExcelErrorNum` | `#NUM!` |
| 离线模式尝试写入 | `ExcelError.ExcelErrorRef` | `#REF!` |

### 线程安全与波动性

| 标记 | 含义 |
|------|------|
| `IsThreadSafe = true` | Excel 可并行计算多个实例 |
| `IsVolatile = false` | 仅在输入参数变化时重算（默认，大多数函数） |
| `IsVolatile = true` | 每次工作表重算都执行（仅 `SYNCSTATUS`） |

---

## 3. 函数详情

### 3.1 BOMEXPAND

```
=BOMEXPAND(itemCode, [asOfDate], [versionState])
```

**展开指定物料的完整 BOM 结构，返回多层级扁平列表。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCode` | string | ✅ | — | 物料编码 |
| `asOfDate` | date/string/number | ❌ | 今天 | 截止日期。支持日期值、`"2026-07-12"` 格式、序列号 |
| `versionState` | string | ❌ | `"Released"` | 版本过滤：`"Draft"` / `"Released"` / `"All"` |

**返回值**: 二维数组，列定义如下：

| 列 | 标题 | 类型 | 说明 |
|:--:|------|------|------|
| 1 | Level | int | 层级（0 = 顶层物料） |
| 2 | ItemCode | string | 物料编码 |
| 3 | Description | string | 物料描述 |
| 4 | Quantity | double | 汇总用量（父级用量 × 本级用量） |
| 5 | Unit | string | 单位 |
| 6 | Source | string | `"Make"` / `"Buy"` |

**行为差异**:

| Excel 版本 | 行为 |
|-----------|------|
| Excel 365 | 动态数组自动溢出到相邻单元格 |
| Excel 2016/2019 | 需选中足够区域后按 `Ctrl+Shift+Enter`。超出 1000 行的数据截断并在末行提示 |

**示例**:

```
=BOMEXPAND("MAT-001")                        → 展开 MAT-001，只看 Released 版本，截止今天
=BOMEXPAND("MAT-001", "2026-01-01")          → 展开 MAT-001，截止 2026-01-01
=BOMEXPAND("MAT-001", , "All")               → 展开 MAT-001，含 Draft + Released
```

**错误**:

| 条件 | 返回值 |
|------|--------|
| `itemCode` 为空 | `#N/A` |
| 物料不存在 | `#N/A` |
| BOM 层级超过 20 级 | `#NUM!` |

---

### 3.2 BOMCOST

```
=BOMCOST(itemCode, [asOfDate])
```

**计算指定物料的汇总成本（本物料 + 所有子物料成本 × 用量之和）。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCode` | string | ✅ | — | 物料编码 |
| `asOfDate` | date/string/number | ❌ | 今天 | 成本计算的截止日期（影响价格版本选择） |

**返回值**: `double` — 汇总成本（以基础货币为单位）

**示例**:

```
=BOMCOST("MAT-001")                           → 1234.56
=BOMCOST("MAT-001", "2026-06-01")             → 1180.00（历史成本）
```

**错误**:

| 条件 | 返回值 |
|------|--------|
| 物料不存在 | `#N/A` |
| 某个子物料无价格数据 | 该子物料成本 = 0，不影响汇总 |

---

### 3.3 PRICELOOKUP

```
=PRICELOOKUP(itemCode, supplierCode, [asOfDate])
```

**查询指定物料从指定供应商的最新单价。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCode` | string | ✅ | — | 物料编码 |
| `supplierCode` | string | ✅ | — | 供应商编码 |
| `asOfDate` | date | ❌ | 今天 | 价格查询日期 |

**返回值**: `double` — 单价

**示例**:

```
=PRICELOOKUP("MAT-001", "SUP-HK-01")          → 12.50
=PRICELOOKUP("MAT-001", "SUP-HK-01", TODAY()) → 12.50
```

**错误**:

| 条件 | 返回值 |
|------|--------|
| 物料或供应商不存在 | `#N/A` |
| 该供应商无此物料的价格 | `#N/A` |

---

### 3.4 INVENTORYQTY

```
=INVENTORYQTY(itemCode, [warehouseId])
```

**查询指定物料的当前库存总量。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCode` | string | ✅ | — | 物料编码 |
| `warehouseId` | string | ❌ | 全部仓库汇总 | 指定仓库编码 |

**返回值**: `double` — 库存数量

**示例**:

```
=INVENTORYQTY("MAT-001")                      → 5000（所有仓库合计）
=INVENTORYQTY("MAT-001", "WH-SH-01")          → 1200（仅上海仓）
```

**错误**:

| 条件 | 返回值 |
|------|--------|
| 物料不存在 | `#N/A` |
| 仓库不存在 | `#N/A` |
| 离线模式且无缓存 | `#REF!` |

---

### 3.5 VARIANCECHECK

```
=VARIANCECHECK(itemCodeA, [dateA], [itemCodeB], [dateB])
```

**比较同一物料在两个时间点（或两个物料）BOM 结构和价格的差异。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCodeA` | string | ✅ | — | 基准物料编码 |
| `dateA` | date/string | ❌ | TODAY() | 基准 BOM 日期（默认今天） |
| `itemCodeB` | string | ❌ | =itemCodeA | 对比物料编码（不填则与自身对比） |
| `dateB` | date/string | ❌ | TODAY()-1 | 对比 BOM 日期（默认昨天，用于时间点对比） |

**返回值**: 二维数组（表头: NodeCode, ChangeType, Dimension, OldValue, NewValue）

**示例**:

```
=VARIANCECHECK("MAT-001")                                    → 默认: 今天 vs 昨天（时间点对比）
=VARIANCECHECK("MAT-001", "2026-06-01", "MAT-001", "2026-07-01") → 同物料两时间点对比（需显式指定 itemCodeB）
=VARIANCECHECK("MAT-001", TODAY(), "MAT-002", TODAY())        → 两物料同日期交叉对比
```

**v1.1 更新**: 价格差异管线已实现（Sprint 5），VARIANCECHECK 同时输出结构差异和价格差异。

---

### 3.6 ALERTCHECK

```
=ALERTCHECK([itemCode])
```

**检查物料的预警状态（价格异常、数量变化）。**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:--:|--------|------|
| `itemCode` | string | ❌ | 全部物料 | 物料编码（不填则检查全部） |

**返回值**: 二维数组（表头: Severity, Message, Rule, NodeCode）或 `"No alerts"` 字符串

**预警规则**:

| 规则 | 条件 | 级别 |
|------|------|------|
| BOM_NODE_REMOVED | BOM 节点被移除 | Warning |
| BOM_NODE_ADDED | BOM 新增节点 | Info |
| PRICE_CHANGE_SEVERE | 价格波动 > 25% | Error |
| PRICE_CHANGE_WARNING | 价格波动 > 10% | Warning |
| BOM_QTY_LARGE_CHANGE | BOM 用量变化 > 50% | Warning |

**示例**:

```
=ALERTCHECK("MAT-001")        → 检查指定物料预警
=ALERTCHECK()                 → 检查全部物料预警（无参数）
```

**v1.1 更新**: 已修复为真正展开 BOM 并检查价格历史变化（Sprint 5）。

---

### 3.7 ORDERSTATUS

```
=ORDERSTATUS(itemCode)
```

**查询指定物料的最新采购订单状态。**

| 参数 | 类型 | 必填 | 说明 |
|------|------|:--:|------|
| `itemCode` | string | ✅ | 物料编码 |

**返回值**: `string`

| 值 | 含义 |
|----|------|
| `"InStock"` | 有库存，无在途订单 |
| `"OnOrder: {n}"` | 有 {n} 个在途采购订单 |
| `"Shortage: {n}"` | 缺货 {n} 件，无在途订单 |
| `"N/A"` | 无数据 |

---

### 3.8 SYNCSTATUS

```
=SYNCSTATUS()
```

**返回最近一次数据同步时间。无参数，易变函数。**

**返回值**: `string`

| 值 | 含义 |
|----|------|
| `"Never synced"` | 从未执行过数据同步 |
| `"Synced Xm ago"` | 最近同步在 X 分钟前（<1 小时） |
| `"Synced X.Xh ago"` | 最近同步在 X.X 小时前（<24 小时） |
| `"Synced yyyy-MM-dd HH:mm"` | 最近同步的具体时间（≥24 小时前） |
| `#VALUE!` | 查询失败（数据库不可用等） |

> **注意**: V1.1 版本 SYNCSTATUS 仅返回同步时间戳，不含在线/离线状态判断。
> Dashboard 中的在线/离线指示由 `NetworkMonitor` 独立提供。

---

## 4. 错误码速查

| Excel 错误 | 含义 | 常见原因 |
|-----------|------|---------|
| `#N/A` | 数据不可用 | 物料/供应商不存在、价格未配置 |
| `#VALUE!` | 参数类型错误 | 传了文本给日期参数但无法解析 |
| `#NUM!` | 数值超出限制 | BOM 层级超过 20 级、循环引用 |
| `#REF!` | 引用无效或操作被禁止 | 离线模式尝试写入、引用的版本已删除 |
| `#NAME?` | 函数名错误 | 插件未加载或函数名拼写错误 |

---

## 5. 版本兼容性

| 功能 | Excel 2016 | Excel 2019 | Excel 365 |
|------|:--:|:--:|:--:|
| 数组公式自动溢出 | ❌ 需 `Ctrl+Shift+Enter` | ❌ 需 `Ctrl+Shift+Enter` | ✅ 动态数组 |
| `BOMEXPAND` 大数据集 | 截断 >1000 行并提示 | 同 2016 | 自动溢出全部 |
| `VARIANCECHECK` 数组输出 | 返回单文本摘要 | 同 2016 | 返回结构化数组 |
| 其他单值函数 | 正常 | 正常 | 正常 |

> 详细兼容性矩阵见 [specification.md §13](./specification.md#13-excel-版本兼容矩阵)

---

## 6. 与 specification.md 的关系

本文档是 UDF API 的**权威参考**（canonical reference）。`specification.md §8` 定义了函数的设计意图和宏观分类。若两者在签名或语义上不一致，以本文档为准（本文档更接近实现）。
