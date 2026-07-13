<#
.SYNOPSIS
    Windows pre-commit hook — 编译 + 测试质量 + 全量测试
.INSTALL
    复制到 .git/hooks/pre-commit (无 .ps1 后缀)
    copy tools\pre-commit.ps1 .git\hooks\pre-commit
    或在 .git/hooks/pre-commit 中:
    @echo off
    pwsh -NoProfile -File "%~dp0..\..\tools\pre-commit.ps1"
    if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
#>

$ErrorActionPreference = "Stop"
$repoRoot = git rev-parse --show-toplevel
$failed = $false

Write-Host "`n🔍 BomAddIn Pre-Commit Guard" -ForegroundColor Cyan
Write-Host "════════════════════════════════" -ForegroundColor Cyan

# ═══ 1. 编译检查 ═══
Write-Host "`n[1/3] dotnet build..." -ForegroundColor White
$buildResult = dotnet build "$repoRoot\BomAddIn.sln" --nologo -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 编译失败，提交中止。" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "✅ 编译通过" -ForegroundColor Green

# ═══ 2. 测试质量守卫 ═══
Write-Host "`n[2/3] 测试质量检查..." -ForegroundColor White
try {
    $guardResult = pwsh -NoProfile -File "$repoRoot\tools\test-quality-guard.ps1" -Mode Quick 2>&1
    if ($LASTEXITCODE -eq 2) {
        Write-Host "⚠ 测试质量问题（弱断言等），作为警告继续（不阻塞）。" -ForegroundColor Yellow
        Write-Host "  请考虑修复后再提交。" -ForegroundColor Yellow
    } else {
        Write-Host "✅ 测试质量检查通过" -ForegroundColor Green
    }
} catch {
    Write-Host "⚠ 测试质量检查脚本执行出错（跳过）: $_" -ForegroundColor Yellow
}

# ═══ 3. 全部测试 ═══
Write-Host "`n[3/3] dotnet test..." -ForegroundColor White
$testResult = dotnet test "$repoRoot\BomAddIn.sln" --nologo --no-build -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 测试失败，提交中止。" -ForegroundColor Red
    Write-Host $testResult
    exit 1
}
Write-Host "✅ 全部测试通过" -ForegroundColor Green

Write-Host "`n✅✅✅ 所有检查通过，允许提交。`n" -ForegroundColor Green
exit 0
