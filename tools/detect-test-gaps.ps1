<#
.SYNOPSIS
    检测测试覆盖缺口 — 列出所有源文件和对应的测试文件，标记缺失覆盖。
    输出 JSON 和 Markdown 格式报告。

.EXAMPLE
    .\detect-test-gaps.ps1 -OutputMarkdown
    .\detect-test-gaps.ps1 -OutputJson -JsonPath coverage-gaps.json
#>
param(
    [switch]$OutputMarkdown,
    [switch]$OutputJson,
    [string]$JsonPath = "test-gaps.json"
)

$repoRoot = Split-Path -Parent $PSScriptRoot

# 映射：源文件模式 → 测试文件模式
$mappings = @(
    # Core Services
    @{ Src = "src/BomAddIn.Core/Services"; Test = "tests/BomAddIn.UnitTests"; Pattern = "^(I?)(\w+)\.cs$" }
    # Data Repositories
    @{ Src = "src/BomAddIn.Data/Repositories"; Test = "tests/BomAddIn.IntegrationTests"; Pattern = "^(I?)(\w+)Repository\.cs$" }
    # UI Components
    @{ Src = "src/BomAddIn/Dashboard"; Test = "tests/BomAddIn.UnitTests"; Pattern = "^(\w+)\.cs$" }
    @{ Src = "src/BomAddIn/UI"; Test = "tests/BomAddIn.UnitTests"; Pattern = "^(\w+)\.cs$" }
    # Infrastructure
    @{ Src = "src/BomAddIn.Infrastructure/Security"; Test = "tests/BomAddIn.UnitTests"; Pattern = "^(\w+)\.cs$" }
    @{ Src = "src/BomAddIn.Infrastructure/Network"; Test = "tests/BomAddIn.UnitTests"; Pattern = "^(\w+)\.cs$" }
)

$results = @()

foreach ($map in $mappings) {
    $srcDir = Join-Path $repoRoot $map.Src
    $testDir = Join-Path $repoRoot $map.Test

    if (-not (Test-Path $srcDir)) { continue }

    # 获取所有源文件（排除接口/抽象类/程序集信息）
    $srcFiles = Get-ChildItem $srcDir -Filter "*.cs" -File |
        Where-Object { $_.Name -notmatch '^I[A-Z]' -and $_.Name -notmatch 'AssemblyInfo|\.Designer\.' }

    # 获取所有测试文件
    $testFiles = @()
    if (Test-Path $testDir) {
        $testFiles = Get-ChildItem $testDir -Filter "*Tests.cs" -Recurse -File | ForEach-Object { $_.Name }
    }

    foreach ($src in $srcFiles) {
        $baseName = $src.BaseName
        $expectedTestName1 = "${baseName}Tests.cs"
        $expectedTestName2 = "${baseName}EdgeCaseTests.cs"

        $hasTest = ($testFiles -contains $expectedTestName1) -or ($testFiles -contains $expectedTestName2)

        $results += [PSCustomObject]@{
            SourceFile   = $src.Name
            SourcePath   = $src.FullName.Replace($repoRoot, '.')
            Category     = if ($map.Src -match "Services") { "Service" }
                      elseif ($map.Src -match "Repositories") { "Repository" }
                      elseif ($map.Src -match "Dashboard") { "Dashboard" }
                      elseif ($map.Src -match "UI") { "UI" }
                      elseif ($map.Src -match "Security|Network") { "Infrastructure" }
                      else { "Other" }
            HasTest      = $hasTest
            TestFiles    = if ($hasTest) {
                @($expectedTestName1, $expectedTestName2 | Where-Object { $testFiles -contains $_ }) -join ', '
            } else { "NONE" }
        }
    }
}

# ── 输出 ──

$covered = ($results | Where-Object HasTest).Count
$uncovered = ($results | Where-Object { -not $_.HasTest }).Count
$total = $results.Count
$pct = if ($total -gt 0) { [math]::Round($covered / $total * 100, 1) } else { 0 }

Write-Host "`n═════════════════════════════════" -ForegroundColor Cyan
Write-Host "  测试覆盖缺口报告" -ForegroundColor Cyan
Write-Host "═════════════════════════════════" -ForegroundColor Cyan
Write-Host "  源文件总数: $total"
Write-Host "  有测试: $covered ($pct%)" -ForegroundColor Green
Write-Host "  无测试: $uncovered" -ForegroundColor $(if ($uncovered -gt 0) { "Red" } else { "Green" })

if ($uncovered -gt 0) {
    Write-Host "`n  未覆盖文件:" -ForegroundColor Red
    $results | Where-Object { -not $_.HasTest } | ForEach-Object {
        Write-Host "    ❌ $($_.Category)/$($_.SourceFile) — $($_.SourcePath)" -ForegroundColor Red
    }
}

# Markdown 输出
if ($OutputMarkdown) {
    $md = @"
## 测试覆盖缺口报告

**生成时间**: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
**覆盖率**: $covered/$total ($pct%)

### 未覆盖文件

| 类别 | 文件 | 路径 |
|------|------|------|
$(
    ($results | Where-Object { -not $_.HasTest } | ForEach-Object {
        "| $($_.Category) | $($_.SourceFile) | $($_.SourcePath) |"
    }) -join "`n"
)

### 全部文件

| 类别 | 文件 | 有测试 | 测试文件 |
|------|------|:--:|------|
$(
    ($results | Sort-Object Category, SourceFile | ForEach-Object {
        $check = if ($_.HasTest) { "✅" } else { "❌" }
        "| $($_.Category) | $($_.SourceFile) | $check | $($_.TestFiles) |"
    }) -join "`n"
)
"@
    $mdPath = Join-Path $repoRoot "docs/test-coverage-gaps.md"
    $md | Out-File $mdPath -Encoding UTF8
    Write-Host "`n  Markdown 报告: $mdPath" -ForegroundColor Green
}

# JSON 输出
if ($OutputJson) {
    $jsonPathFull = Join-Path $repoRoot $JsonPath
    $results | ConvertTo-Json -Depth 3 | Out-File $jsonPathFull -Encoding UTF8
    Write-Host "  JSON 报告: $jsonPathFull" -ForegroundColor Green
}

exit $(if ($uncovered -gt 0) { 1 } else { 0 })
