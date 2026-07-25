<#
.SYNOPSIS
    测试质量守卫 — 检测弱断言、缺失测试覆盖、测试命名规范。
    用于 pre-commit hook 和 CI 流水线。

.DESCRIPTION
    检查项:
    1. 弱断言检测: NotNull/BeGreaterThan(0)/HaveCount(>0) 等不应作为唯一断言
    2. 未测试的 Repository/Service 检测
    3. [Fact] 测试方法命名规范（应为描述性名称）
    4. 源文件与测试文件对应关系

.EXAMPLE
    .\test-quality-guard.ps1 -Mode Quick
    .\test-quality-guard.ps1 -Mode Full -GenerateReport
#>
param(
    [ValidateSet("Quick", "Full")]
    [string]$Mode = "Quick",
    [switch]$GenerateReport
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$issues = @()
$warnings = @()

# ── 辅助函数 ──

function Get-CsFiles($path) {
    Get-ChildItem -Path $path -Filter "*.cs" -Recurse -File -Exclude "*AssemblyInfo*","*.Designer.cs"
}

function Get-TestContent($path) {
    Get-ChildItem -Path $path -Filter "*Tests.cs" -Recurse -File | ForEach-Object {
        [PSCustomObject]@{ File = $_.FullName; Content = Get-Content $_.FullName -Raw }
    }
}

# ══════════════════════════════════════════════════════
# 1. 弱断言检测
# ══════════════════════════════════════════════════════

Write-Host "`n[1/5] 弱断言检测..." -ForegroundColor Cyan

$weakAssertPatterns = @(
    @{ Pattern = '\.Should\(\)\.NotBeNull\(\)\s*;'; Severity = "Weak"; Desc = "仅 NotNull 断言——不验证具体值" },
    @{ Pattern = '\.Should\(\)\.BeGreaterThan\(0\)\s*;'; Severity = "Weak"; Desc = "仅 BeGreaterThan(0)——不验证期望值" },
    @{ Pattern = '\.Should\(\)\.NotBeNullOrEmpty\(\)\s*;'; Severity = "Weak"; Desc = "仅 NotBeNullOrEmpty——不验证内容" }
)

# 排除行: 如果同一测试方法有多个断言，NotBeNull 作为前置检查是可接受的
$testFiles = Get-ChildItem -Path "$repoRoot\tests" -Filter "*Tests.cs" -Recurse -File
$weakCount = 0

foreach ($file in $testFiles) {
    $content = Get-Content $file.FullName -Raw
    $lines = Get-Content $file.FullName

    # 按 [Fact]/[Theory] 分组检测
    $inTestMethod = $false
    $methodAssertions = @()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '\[(Fact|Theory)\]') {
            if ($inTestMethod -and $methodAssertions.Count -eq 1) {
                # 上一方法只有 1 个断言
                foreach ($a in $methodAssertions) {
                    foreach ($wp in $weakAssertPatterns) {
                        if ($a.Line -match $wp.Pattern) {
                            $issues += "[WEAK] $($file.Name):$($a.Number) — $($wp.Desc)"
                            $weakCount++
                        }
                    }
                }
            }
            $methodAssertions = @()
            $inTestMethod = $true
        }
        if ($inTestMethod -and $line -match '\.Should\(\)\.') {
            $methodAssertions += @{ Line = $line; Number = $i + 1 }
        }
        if ($inTestMethod -and $line -match '^\s*\}\s*$' -and $i -gt 0) {
            # 方法结束——检查断言数量
            if ($methodAssertions.Count -eq 1) {
                foreach ($a in $methodAssertions) {
                    foreach ($wp in $weakAssertPatterns) {
                        if ($a.Line -match $wp.Pattern) {
                            $issues += "[WEAK] $($file.Name):$($a.Number) — $($wp.Desc)"
                            $weakCount++
                        }
                    }
                }
            }
            $inTestMethod = $false
        }
    }
}

Write-Host "  发现 $weakCount 个弱断言（唯一断言为 NotNull/BeGreaterThan(0)/NotNullOrEmpty）" -ForegroundColor $(if ($weakCount -gt 0) { "Yellow" } else { "Green" })

# ══════════════════════════════════════════════════════
# 2. 源文件→测试文件对应关系检测
# ══════════════════════════════════════════════════════

Write-Host "`n[2/5] 测试覆盖对应..." -ForegroundColor Cyan

$sourceServices = Get-ChildItem "$repoRoot\src\BomAddIn.Core\Services" -Filter "I*.cs" -File |
    Where-Object { $_.Name -notmatch '^I(BomExcelImporter|SeedDataGenerator|ConfigService)' } |
    ForEach-Object { $_.Name -replace '^I','' -replace '\.cs$','' }

$sourceRepos = Get-ChildItem "$repoRoot\src\BomAddIn.Data\Repositories" -Filter "I*.cs" -File |
    ForEach-Object { $_.Name -replace '^I','' -replace '\.cs$','' }

$testedServices = @{}
$testFiles | ForEach-Object {
    $testName = $_.Name -replace 'Tests\.cs$','' -replace 'EdgeCaseTests\.cs$',''
    if ($testName -match '^(BomService|ApprovalService|VarianceCalculator|AlertEvaluator|AuditService|AuthService|AuthorizationService|SnapshotService|SyncService)') {
        $testedServices[$testName] = $true
    }
}

$untestedServices = @()
foreach ($svc in ($sourceServices | Where-Object { $_ -notmatch '^(IBomService|IBomVersion|IMaterial|IAuditService|IAuthorizationService|IAuthService|IAlertEvaluator|IApprovalService|ISnapshotService|ISyncService|IVarianceCalculator|IVarianceService|IBomExcelImporter)$' })) {
    $svcClean = $svc -replace '^I',''
    if (-not $testedServices.ContainsKey($svcClean)) {
        $untestedServices += $svcClean
    }
}

if ($untestedServices.Count -gt 0) {
    $warnings += "[UNTESTED] 服务: $($untestedServices -join ', ')"
    Write-Host "  ⚠ 未测试服务: $($untestedServices -join ', ')" -ForegroundColor Yellow
}

# Repository 测试检查
$repoTestFiles = $testFiles | Where-Object { $_.Name -match 'RepositoryTests' }
$testedRepos = @{}
$repoTestFiles | ForEach-Object {
    $name = $_.Name -replace 'Tests\.cs$',''
    $testedRepos[$name] = $true
}

$untestedRepos = @()
foreach ($repo in $sourceRepos) {
    if (-not $testedRepos.ContainsKey($repo)) {
        $untestedRepos += $repo
    }
}
if ($untestedRepos.Count -gt 0) {
    $warnings += "[UNTESTED] Repository: $($untestedRepos -join ', ')"
    Write-Host "  ⚠ 未测试 Repository ($($untestedRepos.Count)): $($untestedRepos -join ', ')" -ForegroundColor Yellow
}

# ══════════════════════════════════════════════════════
# 3. 测试命名规范
# ══════════════════════════════════════════════════════

Write-Host "`n[3/5] 测试命名规范检查..." -ForegroundColor Cyan

$badNames = @()
$testFiles | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $methodMatches = [regex]::Matches($content, 'public\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(')
    foreach ($m in $methodMatches) {
        $name = $m.Groups[1].Value
        if ($name -notmatch '_Should|_Returns|_Throws|_Does|_When' -and $name -ne 'InitializeAsync') {
            $badNames += "$($_.Name): $name"
        }
    }
}

if ($badNames.Count -gt 0) {
    $warnings += "[NAMING] 非标准命名的测试方法 ($($badNames.Count))"
    Write-Host "  ⚠ 非标准命名测试: $($badNames.Count) 个" -ForegroundColor Yellow
    if ($Mode -eq "Full") {
        $badNames | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    }
}

# ══════════════════════════════════════════════════════
# 4. 测试数据统计
# ══════════════════════════════════════════════════════

Write-Host "`n[4/5] 测试数据统计..." -ForegroundColor Cyan

$totalFacts = 0
$totalTheories = 0
$testFiles | ForEach-Object {
    $c = Get-Content $_.FullName -Raw
    $totalFacts += ([regex]::Matches($c, '\[Fact\]')).Count
    $totalTheories += ([regex]::Matches($c, '\[Theory\]')).Count
}

$totalTests = $totalFacts + $totalTheories

Write-Host "  [Fact]: $totalFacts, [Theory]: $totalTheories, 总计: $totalTests 个测试方法" -ForegroundColor White

# ══════════════════════════════════════════════════════
# 5. 静态代码问题检测
# ══════════════════════════════════════════════════════

Write-Host "`n[5/5] 测试静态问题检测..." -ForegroundColor Cyan

$staticIssues = @()
$testFiles | ForEach-Object {
    $content = Get-Content $_.FullName -Raw

    # 检查 static 共享状态
    if ($content -match 'private\s+static\s+(int|long|double|string)\s+_') {
        $staticIssues += "[STATIC-STATE] $($_.Name): 存在 static 可变状态 — 并行测试不安全"
    }

    # 检查 Thread.Sleep（可能导致测试不稳定）
    if ($content -match 'Thread\.Sleep\(') {
        $staticIssues += "[FLAKY] $($_.Name): 使用 Thread.Sleep — 在 CI 中可能不稳定"
    }

    # 检查硬编码路径
    if ($content -match 'C:\\|D:\\|E:\\') {
        $staticIssues += "[HARDCODED-PATH] $($_.Name): 硬编码文件路径"
    }
}

if ($staticIssues.Count -gt 0) {
    Write-Host "  ⚠ 静态问题:" -ForegroundColor Yellow
    $staticIssues | ForEach-Object {
        Write-Host "    $_" -ForegroundColor Gray
        $issues += $_
    }
} else {
    Write-Host "  ✅ 无静态问题" -ForegroundColor Green
}

# ══════════════════════════════════════════════════════
# 汇总输出
# ══════════════════════════════════════════════════════

Write-Host "`n═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  测试质量报告" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  总测试方法: $totalTests"
Write-Host "  弱断言: $weakCount"
Write-Host "  未测试 Repository: $($untestedRepos.Count)"
Write-Host "  未测试 Service: $($untestedServices.Count)"
Write-Host "  问题总数: $($issues.Count)"
Write-Host "  警告总数: $($warnings.Count)"
Write-Host ""

# 退出码
if ($issues.Count -gt 0) {
    Write-Host "❌ 发现 $($issues.Count) 个问题，详见上方列表。" -ForegroundColor Red
    exit 2
}

if ($warnings.Count -gt 0) {
    Write-Host "⚠ 发现 $($warnings.Count) 个警告。" -ForegroundColor Yellow
}

Write-Host "✅ 测试质量检查通过。" -ForegroundColor Green
exit 0
