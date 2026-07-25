# 贡献指南

## 开发环境

- **IDE**: Visual Studio 2022 / Rider（需安装 Excel-DNA 插件支持）
- **SDK**: .NET Framework 4.7.2 + .NET Standard 2.0
- **Excel**: Microsoft Excel 2019+（32/64 bit）
- **测试**: `dotnet test` 运行 xUnit 测试套件

## 分支策略

| 分支 | 用途 |
|------|------|
| `main` | 稳定发布分支 |
| `develop` | 日常开发集成 |
| `feature/*` | 功能开发 |
| `fix/*` | 缺陷修复 |

## 提交规范

```
<type>(<scope>): <subject>

type: feat | fix | refactor | test | docs | chore | perf
scope: core | data | infra | ui | udf | build
```

示例: `feat(core): 添加 BOM 展开层级过滤参数`

## 代码规范

1. 遵循 `.editorconfig` 中定义的 C# 代码风格
2. 所有公共 API 必须有 XML 文档注释
3. 新增功能必须附带单元测试（覆盖率 ≥ 80%）
4. UDF 函数必须用 `[ExcelFunction]` 显式标记
5. 所有 COM 调用必须经 `ExcelThreadDispatcher` 调度

## 架构约束

- **4 层架构**: UI → Service → Engine → Data
- **禁止跨层调用**: UI 不得直接访问 Data 层
- **Core 零外部依赖**: `BomAddIn.Core` 不引用任何 NuGet 包
- **DI 注册**: 所有服务通过 `ServiceConfigurator` 统一注册

## 构建与测试

```powershell
# 还原 + 构建
dotnet restore BomAddIn.sln
dotnet build BomAddIn.sln -c Release

# 运行测试
dotnet test BomAddIn.sln --no-build -c Release

# 运行性能基准
dotnet run --project tests/BomAddIn.Benchmarks -c Release
```

## PR 检查清单

- [ ] 代码通过 `dotnet build` 无警告
- [ ] 所有测试通过 `dotnet test`
- [ ] 新增/修改的 UDF 已在 Excel 中手动验证
- [ ] 文档已同步更新（rules/ 下相关规范）
- [ ] 无新增外部 NuGet 依赖（除非经架构评审）
