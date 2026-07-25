# BomAddIn (costsuite)

> 企业级 BOM 管理与成本差异分析 Excel 插件：Excel-DNA + C#，目标 10 万级物料 / 100 万级节点，离线优先架构。

---

## 安装

1. 从 [Releases](https://github.com/zgrwo/costsuite/releases) 下载 `BomAddIn-packed.xll`
2. Excel → 文件 → 选项 → 加载项 → 转到 → 浏览 → 选择 .xll
3. 本地数据库自动初始化（SQLite），无需额外配置

### 验证安装

在任意单元格输入：
```
=BOM.PARSE("PN001", 1)
→ 展开 BOM 树
```

---

## 模块速览

> 完整签名、参数说明见 **[API 参考](rules/api-reference.md)**；每个函数的详细示例见 **[用户手册](rules/user-manual.md)**。

| 模块 | 做什么 | 试一试 |
|------|------|-------|
| `BOM` | 物料清单管理（解析/展开/差异） | `=BOM.VARIANCE(bom1, bom2)` |
| `Sync` | ERP 数据同步（SAP/Oracle/Excel） | 一键同步工作簿差异 |
| `Dashboard` | WPF 仪表盘（趋势/统计/告警） | 自定义面板视图 |

---

## 使用模式

### 工作表 UDF

```vb
' BOM 解析（单层展开）
=BOM.PARSE("PN001", 1)

' BOM 差异分析（两个版本对比）
=BOM.VARIANCE(bom_version1, bom_version2)

' 查找物料路径（所有使用位置）
=BOM.WHERESUSED("PN005")
```

### VBA 自动化

```vba
' 后台同步 ERP 数据，不阻塞 Excel
Application.Run("SYNC.IMPORT", "MaterialMaster")
' 可设置定时同步间隔
```

### 仪表盘

点击 Ribbon 按钮打开 WPF 仪表盘，实时查看：
- BOM 版本差异趋势
- 成本变化热力图
- 物料缺失告警

---

## 架构特点

```
UI 层 (Ribbon + TaskPane + WPF)
  ↓ ExcelThreadDispatcher（跨线程 COM 调用）
Service 层 (BomService + SyncService)
  ↓ 业务编排，有状态/事务
Engine 层 (VarianceCalculator)
  ↓ 纯计算，零依赖，可独立单元测试
Data 层 (SQLite + DuckDB)
  离线优先：本地缓存 + 恢复后自动同步
```

- **SQLite**：CRUD 操作（物料主数据、BOM 版本）
- **DuckDB**：分析查询（差异聚合、路径展开）
- **BFS 展开**：替代递归 CTE，防止路径爆炸
- **离线优先**：网络断开自动切换本地，恢复后自动同步

---

## 错误处理

| 场景 | 行为 |
|------|------|
| BOM 节点不存在 | `#VALUE!` |
| 物料编号格式非法 | `#VALUE!` |
| 数据库连接失败 | 自动降级 SQLite 本地缓存 |
| ERP 同步失败 | Polly 重试 3 次 + Excel 导入备用通道 |
| 线程 COM 冲突 | QueueAsMacro 回调 UI 线程 |

---

## 安全

- **BCrypt 认证**：用户登录密码安全哈希
- **DPAPI 加密**：Windows 数据保护 API 加密敏感字段
- **AES-256**：本地数据库字段级加密
- **审计日志**：AOP 拦截全量操作审计
- **ERP 重试**：Polly 断路器防止同步风暴

---

## 质量保证

- **xUnit + Moq**：单元测试 + 模拟测试
- **BenchmarkDotNet**：性能基准测试（BOM 展开/差异计算）
- **双引擎测试**：SQLite 和 DuckDB 路径独立验证
- **线程安全测试**：压力测试 30min，0 线程异常

---

## 已知限制

- **Windows Only**：依赖 Excel-DNA + COM + DPAPI
- **Excel 2016+**：net472 基准，不支持 Excel 2013 及以下
- **首次同步**：大型 ERP 全量同步可能耗时数分钟（后续增量秒级）

---

## 贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 了解贡献流程（fork → PR → review）。

---

## 许可证

[MIT](LICENSE) © zgrwo

---

## 从源码构建

```bash
# 开发构建
dotnet restore && dotnet build && dotnet test

# 分发构建（生成 .xll）
dotnet build -c Release
ExcelDnaPack BomAddIn.dna
```

---

## 文档索引

| 文档 | 角色 | 内容 |
|------|------|------|
| [API 参考](rules/api-reference.md) | 数字唯一信源 | 8 个 UDF 签名、参数说明 |
| [用户手册](rules/user-manual.md) | 学习教程 | 每个函数详细示例 + 结果解读 |
| [context.md](rules/context.md) | 术语表 | 所有领域术语唯一定义 |
| [project-structure.md](rules/project-structure.md) | 结构地图 | 文件职责与层级关系 |
| [agents.md](agents.md) | 项目宪法 | 架构分层、红线规则、开发流程 |
