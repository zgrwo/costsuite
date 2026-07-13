# Authenticode Signing Script
# Signs the packed .xll file with a code signing certificate.
#
# Usage:
#   .\build\scripts\sign.ps1 -CertificatePath "C:\certs\mycert.pfx" -Password "mypassword"
#   .\build\scripts\sign.ps1 -UseWindowsStore  (uses cert from Windows Certificate Store)
#   .\build\scripts\sign.ps1 -SkipSigning        (creates unsigned ZIP only)

param(
    [string]$CertificatePath,
    [string]$Password,
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
if ($SkipSigning) {
    Write-Host "Signing skipped (-SkipSigning)." -ForegroundColor DarkYellow
} elseif ($UseWindowsStore) {
    Write-Host "Signing with Windows Certificate Store..." -ForegroundColor Yellow
    & signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 $xllFile.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Signing failed. Generating unsigned ZIP." -ForegroundColor Red
    } else {
        Write-Host "Signed successfully." -ForegroundColor Green
    }
} elseif ($CertificatePath) {
    if (-not (Test-Path $CertificatePath)) {
        Write-Host "ERROR: Certificate not found: $CertificatePath" -ForegroundColor Red
        exit 1
    }
    Write-Host "Signing with $CertificatePath..." -ForegroundColor Yellow
    $securePass = if ($Password) {
        ConvertTo-SecureString -String $Password -AsPlainText -Force
    } else {
        Read-Host -AsSecureString "Enter certificate password"
    }
    & signtool sign /fd SHA256 /f $CertificatePath /p $Password /tr http://timestamp.digicert.com /td SHA256 $xllFile.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Signing failed." -ForegroundColor Red
    } else {
        Write-Host "Signed successfully." -ForegroundColor Green
    }
} else {
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
