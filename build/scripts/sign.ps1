# Authenticode Signing Script
# Signs the packed .xll file with a code signing certificate.
#
# Usage:
#   .\build\scripts\sign.ps1 -CertificatePath "C:\certs\mycert.pfx" -Password "mypassword"
#   .\build\scripts\sign.ps1 -UseWindowsStore  (uses cert from Windows Certificate Store)
#   .\build\scripts\sign.ps1 -SkipSigning        (creates unsigned ZIP only)

param(
    [string]$CertificatePath,
    [SecureString]$Password,
    [switch]$UseWindowsStore,
    [switch]$SkipSigning,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$OutputDir = "$RepoRoot\build\output\$Configuration"
$DistDir = "$RepoRoot\build\dist"

Write-Host "=== BOM Suite Signing & Distribution ===" -ForegroundColor Cyan

# Step 1: Find XLL file
$xllFile = Get-ChildItem -Path $OutputDir -Filter "*packed.xll" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $xllFile) {
    Write-Host "ERROR: No packed .xll found in $OutputDir" -ForegroundColor Red
    Write-Host "Run '.\build\scripts\build.ps1 -Configuration Release' first." -ForegroundColor Yellow
    exit 1
}

Write-Host "Found XLL: $($xllFile.Name)" -ForegroundColor Green

# Step 2: Sign
# 公共 signtool 参数（SHA-256 摘要 + DigiCert 时间戳）
$signtoolBase = @('/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')

if ($SkipSigning) {
    Write-Host "Signing skipped (-SkipSigning)." -ForegroundColor DarkYellow
}
elseif ($UseWindowsStore) {
    Write-Host "Signing with Windows Certificate Store..." -ForegroundColor Yellow
    & signtool sign @signtoolBase /a $xllFile.FullName
    if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: Signing failed." -ForegroundColor Red }
    else { Write-Host "Signed successfully." -ForegroundColor Green }
}
elseif ($CertificatePath) {
    if (-not (Test-Path $CertificatePath)) {
        Write-Host "ERROR: Certificate not found: $CertificatePath" -ForegroundColor Red
        exit 1
    }
    Write-Host "Signing with $CertificatePath..." -ForegroundColor Yellow

    # 优先使用 -Password (SecureString)，否则交互式输入（输入时字符不回显）
    $plainPassword = if ($Password) {
        [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
    } else {
        $secureInput = Read-Host -AsSecureString "Enter certificate password"
        [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureInput))
    }

    & signtool sign @signtoolBase /f $CertificatePath /p $plainPassword $xllFile.FullName
    if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: Signing failed." -ForegroundColor Red }
    else { Write-Host "Signed successfully." -ForegroundColor Green }
}
else {
    Write-Host "No certificate provided. Skipping signing." -ForegroundColor DarkYellow
}

# Step 3: Package ZIP
Write-Host ""
Write-Host "Creating distribution ZIP..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$version = "1.1.0"  # Update with actual version
$zipName = "BomAddIn-v$version-$Configuration.zip"
$zipPath = "$DistDir\$zipName"

# Collect all output files
$files = Get-ChildItem -Path $OutputDir -File | Where-Object { $_.Extension -match '\.(xll|dll|dna|config|pdb)$' }

# Create ZIP
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $files.FullName -DestinationPath $zipPath -Force

Write-Host "Distribution ZIP created: $zipPath" -ForegroundColor Green
Write-Host "Files: $($files.Count)" -ForegroundColor Green
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
