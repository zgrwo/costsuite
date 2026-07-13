# BOM Suite Build & Pack Script
# Usage: .\build\scripts\build.ps1 [-Configuration Release|Debug] [-NoTest]
param(
    [string]$Configuration = "Release",
    [switch]$NoTest
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$OutputDir = "$RepoRoot\build\output\$Configuration"

Write-Host "=== BOM Suite Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Output: $OutputDir"
Write-Host ""

# Step 1: Restore
Write-Host "[1/4] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore "$RepoRoot\BomAddIn.sln"
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# Step 2: Build
Write-Host "[2/4] Building solution..." -ForegroundColor Yellow
dotnet build "$RepoRoot\BomAddIn.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Step 3: Test (optional)
if (-not $NoTest) {
    Write-Host "[3/4] Running tests..." -ForegroundColor Yellow
    dotnet test "$RepoRoot\BomAddIn.sln" -c $Configuration --no-build --filter "FullyQualifiedName~UnitTests|FullyQualifiedName~IntegrationTests"
    if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: Tests failed, continuing..." -ForegroundColor DarkYellow }
} else {
    Write-Host "[3/4] Tests skipped (-NoTest)" -ForegroundColor DarkGray
}

# Step 4: Pack with ExcelDnaPack
Write-Host "[4/4] Packing XLL with ExcelDnaPack..." -ForegroundColor Yellow
# Auto-discover ExcelDnaPack from NuGet cache (supports version upgrades without script changes)
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                 elseif ($IsLinux -or $IsMacOS) { "$env:HOME/.nuget/packages" }
                 else { "$env:USERPROFILE\.nuget\packages" }
$packTool = Get-ChildItem -Path "$nugetPackages\exceldnapack" -Recurse -Filter "ExcelDnaPack.exe" -ErrorAction SilentlyContinue `
    | Sort-Object FullName -Descending | Select-Object -First 1

if ($packTool) {
    $dnaFile = "$RepoRoot\src\BomAddIn\bin\$Configuration\net472\BomAddIn-AddIn.dna"
    $outputXll = "$OutputDir\BomAddIn-AddIn-packed.xll"

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    & $packTool.FullName $dnaFile /O $outputXll /Y
    if ($LASTEXITCODE -ne 0) { throw "ExcelDnaPack failed" }

    # Copy all output files
    Copy-Item "$RepoRoot\src\BomAddIn\bin\$Configuration\net472\*.xll" $OutputDir -Force
    Copy-Item "$RepoRoot\src\BomAddIn\bin\$Configuration\net472\*.dll" $OutputDir -Force -Exclude "ExcelDna.*"

    Write-Host ""
    Write-Host "=== Build Complete ===" -ForegroundColor Green
    Write-Host "Output: $OutputDir"
    Get-ChildItem $OutputDir | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "WARNING: ExcelDnaPack.exe not found in NuGet cache. Run 'dotnet restore' first." -ForegroundColor Red
    Write-Host "Manual pack: copy all .dll files alongside the .xll file for distribution."
}

Write-Host ""
Write-Host "Distribution ZIP: Compress the $OutputDir folder." -ForegroundColor Cyan
