# 数据模板 (Template)

> 导出日期: 2026-07-16  
> 来源: `database/dev/bom_data_dev.sqlite`  
> 用途: 数据导入的格式参考

## 文件清单

| 文件 | 表名 | 行数 | 导入顺序 |
|------|------|------|----------|
| `Suppliers.csv` | Suppliers | 20 | 1 |
| `Materials.csv` | Materials | 500 | 2 |
| `Users.csv` | Users | 2 | 3 |
| `BomStructures.csv` | BomStructures | 540 | 4 |
| `BomVersions.csv` | BomVersions | 78 | 5 |
| `Prices.csv` | Prices | 6,000 | 6 |
| `Inventories.csv` | Inventories | 6,000 | 6 |
| `Orders.csv` | Orders | 150 | 6 |
| `Capacities.csv` | Capacities | 7 | 6 |
| `Estimates.csv` | Estimates | 15 | 7 |
| `AppConfig.csv` | AppConfig | 6 | 8 |
| `UserTokens.csv` | UserTokens | 0 | 9 |
| `AuditLogs.csv` | AuditLogs | 0 | 10 |
| `SyncLogs.csv` | SyncLogs | 5 | 10 |
| `DataSnapshots.csv` | DataSnapshots | 0 | 10 |
| `SchemaVersions.csv` | SchemaVersions | 4 | (自动) |

## 导入顺序

必须按依赖关系导入（先导父表，再导子表）：

```
Suppliers ──────────────────────────────────────┐
Materials ──┬── BomStructures ── BomVersions ── Estimates
            ├── Prices ────────── Suppliers
            ├── Inventories
            └── Orders
Users ──────┬── UserTokens
            ├── AuditLogs
            └── BomVersions (ApprovedBy)
```

## 格式说明

- **编码**: UTF-8
- **分隔符**: 逗号 (CSV)
- **表头**: 第一行为列名
- **NULL**: 空值（两个连续逗号 `,,`）
- **日期**: `yyyy-MM-dd` 或 `yyyy-MM-dd HH:mm:ss`
- **OrgId**: 固定为 `1`
