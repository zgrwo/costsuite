# Skill: BOM 数据建模最佳实践

> **TRIGGER**: 修改 `src/BomAddIn.Core/Services/BomService.cs`、`VarianceService.cs`、`VarianceCalculator.cs`、`src/BomAddIn.Data/Repositories/BomNode*`、`src/BomAddIn.Data/Analysis/` 或任何 BOM 结构/版本/展开逻辑时，**必须**先读此 Skill。
>
> **来源**: [OpenBOM 架构](https://www.openbom.com/blog/)、[Inventory_v01](https://github.com/petersonmatiss/Inventory_v01)、[PLMore](https://github.com/PLMore)、[Open Industry Project](https://github.com/Open-Industry-Project/Open-Industry-Project)  
> **适用范围**: BOM 数据结构设计、展开算法选型、版本管理

---

## 1. 核心概念：Reference-Instance 分离

OpenBOM 最关键的架构决策之一：**物料定义**与**BOM 中的使用实例**是两个不同的概念。

```text
┌─────────────────────────┐     ┌─────────────────────────┐
│   Material (Reference)  │     │  BomNode (Instance)     │
│   "物料定义"             │     │  "BOM 中的使用实例"      │
├─────────────────────────┤     ├─────────────────────────┤
│ Code: "M8x20-BOLT"      │◄────│ MaterialId → 引用       │
│ Description: "M8×20螺栓"│     │ ParentBomId → 父节点    │
│ Unit: "PCS"             │     │ Quantity: 4             │
│ Category: "Fastener"    │     │ Position: "A3"          │
│ Weight: 0.015 (kg)      │     │ ScrapRate: 0.02         │
│ (全局唯一，定义一次)     │     │ (每个 BOM 使用都有实例)  │
└─────────────────────────┘     └─────────────────────────┘
```

**为什么重要**:
- 一个螺栓 "M8x20-BOLT" 可能在 200 个 BOM 中使用
- 螺栓的规格变了 → 只需改 Material 表一行
- 但用量/位置/损耗率是每个 BOM 实例特有的 → 放在 BomNode 中

**本项目对应**: [specification.md §4.1](../../../rules/specification.md#41-业务表8-张)
- `Materials` 表 = Reference
- `BomStructures` 表 = Instance（`ParentMaterialId`, `ChildMaterialId`, `Quantity`, `Position`）

## 2. BOM 存储模型选型

| 模型 | 查询单层 | 查询全树 | 插入节点 | 移动子树 | 适用规模 | 本项目 |
|------|---------|---------|---------|---------|---------|--------|
| **邻接表** | O(1) | O(N×深度) | O(1) | O(1) | <10 万节点 | ✅ V1.0 采用 |
| **嵌套集** | O(N) | O(1) | O(N) | O(N) | <100 万节点 | V2.0 候选 |
| **物化路径** | O(logN) | O(logN) | O(路径长度) | O(N) | 任意 | V2.0 候选 |
| **图数据库** | O(1) | O(深度) | O(1) | O(1) | 百万+ | V3.0 候选 |

### 本项目的邻接表实现

```sql
-- BomStructures 表（精简）
CREATE TABLE BomStructures (
    Id              INT PRIMARY KEY,
    OrgId           INT NOT NULL,
    ParentMaterialId INT NOT NULL REFERENCES Materials(Id),
    ChildMaterialId  INT NOT NULL REFERENCES Materials(Id),
    Quantity        DECIMAL(18,6) NOT NULL DEFAULT 1,
    Position        NVARCHAR(50),        -- 位号/参考标识符
    ScrapRate       DECIMAL(5,4) DEFAULT 0,
    ValidFrom       DATE NOT NULL,       -- 生效日期
    ValidTo         DATE,                -- 失效日期 (NULL = 当前有效)
    VersionState    NVARCHAR(20) DEFAULT 'Draft',  -- Draft/Released/Obsolete

    INDEX IX_Bom_Parent (ParentMaterialId, ValidFrom),
    INDEX IX_Bom_Child (ChildMaterialId)
);
```

### BOM 展开：层序遍历

```csharp
// BomService.cs
public List<BomNode> Expand(string itemCode, DateTime? asOfDate = null)
{
    var date = asOfDate ?? DateTime.Today;
    var result = new List<BomNode>();
    var currentLevel = new Queue<BomNode>();

    // 找到根物料
    var root = _repo.GetMaterialByCode(itemCode);
    currentLevel.Enqueue(new BomNode { Material = root, Level = 0, Quantity = 1 });

    while (currentLevel.Count > 0 && result.Count < MAX_NODES)
    {
        var parent = currentLevel.Dequeue();
        result.Add(parent);

        // 批量加载下一层（一次 SQL，不是 N 次）
        var children = _repo.GetChildNodes(parent.Material.Id, date);

        foreach (var child in children)
        {
            var node = new BomNode
            {
                Material = child.Material,
                Level = parent.Level + 1,
                Quantity = parent.Quantity * child.Quantity,  // 汇总用量
                Parent = parent
            };
            currentLevel.Enqueue(node);
        }
    }

    return result;
}
```

**性能要点**:
- **批量加载**: `GetChildNodes` 一次查出所有子节点，不做 N+1
- **IN 查询**: 同层所有父节点用 `WHERE ParentMaterialId IN (id1, id2, ...)`
- **结果缓存**: 同一物料+BOM 版本的结果缓存在 L1 MemoryCache（TTL 5min）

## 3. xBOM 多视图模式

OpenBOM 的核心架构概念：**同一个产品模型，不同视角**。

```text
               ┌──────────────────────┐
               │   统一 BOM 数据模型    │
               │  (Graph of Items)     │
               └──────────┬───────────┘
                          │
        ┌─────────────────┼─────────────────┐
        ▼                 ▼                  ▼
   ┌─────────┐      ┌─────────┐       ┌─────────┐
   │  EBOM   │      │  MBOM   │       │  CBOM   │
   │ 设计视图 │      │ 制造视图 │       │ 成本视图 │
   ├─────────┤      ├─────────┤       ├─────────┤
   │ 按设计结构│     │ 加工序分组│      │ 按成本中心│
   │ 组织     │      │ 含工装夹具│      │ 汇总价格 │
   └─────────┘      └─────────┘       └─────────┘
```

**本项目如何支持**（V1.0 建议预留）:

```sql
-- 在 BomStructures 加一个列标记视图类型
ALTER TABLE BomStructures ADD BomViewType NVARCHAR(10) DEFAULT 'EBOM';
-- 可选值: 'EBOM', 'MBOM', 'CBOM'
```

```csharp
public enum BomViewType { EBOM, MBOM, CBOM }

// UDF 支持视图参数
[ExcelFunction(Name = "BOMEXPAND")]
public static object[,] BomExpand(
    string itemCode,
    DateTime? asOfDate = null,
    string viewType = "EBOM")  // ← V1.0 可加此参数
{ ... }
```

## 4. BOM 版本生命周期

```
┌──────┐    创建     ┌──────────┐    审批通过    ┌──────────┐
│ Draft │ ─────────► │ Released │ ─────────────► │ Obsolete │
│ 草稿  │            │ 已发布    │   归档/替代    │ 已废弃    │
└──────┘            └──────────┘                └──────────┘
    │                     │
    │ 可自由编辑          │ 只读（审批后可创建新 Draft 修订）
    │ 不参与 MRP         │ 参与 MRP / 差异分析
```

**实现要点**:
- `ValidFrom` / `ValidTo` 实现**时间双表**（Bitemporal）：可以查询 "2025 年 6 月的 BOM 长什么样"
- 每次 Released 后创建新 Draft 修订 → 版本号递增
- 差异分析总是对比两个 Released 版本或 Released vs Draft

## 5. Excel BOM 导入最佳实践

借鉴 Inventory_v01 的智能列检测：

```csharp
public class BomExcelImporter
{
    // 自动检测列映射（模糊匹配中文/英文表头）
    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["ItemCode"] = new[] { "物料编码", "Item Code", "Code", "编码", "料号" },
        ["Description"] = new[] { "描述", "Description", "Desc", "名称", "Name" },
        ["Quantity"] = new[] { "数量", "Quantity", "Qty", "用量" },
        ["Unit"] = new[] { "单位", "Unit", "UOM" },
        ["Level"] = new[] { "层级", "Level", "BOM Level" },
    };

    public ImportResult Import(string filePath)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets[0];

        // 1. 自动检测表头行
        int headerRow = DetectHeaderRow(sheet);

        // 2. 模糊匹配列名
        var mapping = MapColumns(sheet, headerRow, ColumnAliases);

        // 3. 验证必填列
        if (!mapping.ContainsKey("ItemCode"))
            return ImportResult.Fail("未找到物料编码列。请检查表头是否包含：物料编码、Item Code");

        // 4. 逐行读取 + 验证
        // ...
    }
}
```

## 6. 自检清单

- [ ] BOM 查询使用批量 IN 而非逐行 N+1
- [ ] BOM 展开结果有缓存（L1 MemoryCache）
- [ ] 邻接表已足够 V1.0 规模；百万级时评估嵌套集/图数据库
- [ ] `ValidFrom`/`ValidTo` 索引正确（复合索引覆盖查询模式）
- [ ] Excel 导入支持中文/英文表头的智能列检测
- [ ] 版本生命周期状态转换有业务规则校验（不允许 Released→Draft 跳过审批）
- [ ] 预留了 xBOM 多视图扩展点（`BomViewType` 列或 Tags）

## 7. 参考

- [OpenBOM Architecture: Graph-based, Multi-Tenant, Cloud-Native](https://www.openbom.com/blog/day-3-openbom-architecture-explained-multi-tenant-cloud-native-graph-based-collaborative)
- [OpenBOM: xBOM Multi-Discipline Structures](https://www.openbom.com/blog/day-11-xbom-managing-multi-discipline-and-multi-lifecycle-structures)
- [Inventory_v01: BOM Import + Inventory Management](https://github.com/petersonmatiss/Inventory_v01)
- [PLMore: Open-Source PLM with EBOM/MBOM/SBOM](https://github.com/PLMore)
