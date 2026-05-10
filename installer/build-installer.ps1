# SG Revit Addin - Build Installer
# Compiles both projects in Release mode and then builds the Inno Setup installer.
#
# Prerequisites:
#   - Inno Setup 6.x installed (https://jrsoftware.org/isinfo.php)
#   - .NET SDK installed
#
# Usage:
#   powershell -File installer\build-installer.ps1
#   powershell -File installer\build-installer.ps1 -SkipBuild    # only rebuild installer
#   powershell -File installer\build-installer.ps1 -Sign -CertPath "C:\certs\cert.pfx" -CertPassword "pass"

param(
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertPath,
    [string]$CertPassword
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SG Revit Addin - Installer Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── Step 1: Build both projects ──
if (-not $SkipBuild) {
    Write-Host "`n[1/4] Building SgRevit24 (Revit 2023-2024)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SgRevit24\SgRevit24.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "SgRevit24 build failed" }

    Write-Host "`n[2/4] Building SgRevit25 (Revit 2025-2026)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SgRevit25\SgRevit25.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "SgRevit25 build failed" }
} else {
    Write-Host "`n[1-2/4] Skipping build (using existing Release output)" -ForegroundColor DarkGray
}

# ── Step 2: Verify build output exists ──
Write-Host "`n[3/4] Verifying build output..." -ForegroundColor Yellow

$requiredFiles = @(
    "$SolutionRoot\src\SgRevit24\bin\Release\SgRevit24.dll",
    "$SolutionRoot\src\SgRevit24\bin\Release\System.Text.Json.dll",
    "$SolutionRoot\src\SgRevit25\bin\Release\SgRevit25.dll",
    "$SolutionRoot\src\SgRevit25\bin\Release\SgRevit25.deps.json"
)

foreach ($f in $requiredFiles) {
    if (-not (Test-Path $f)) {
        throw "Missing required file: $f"
    }
}

# Check for icon
$iconPath = "$InstallerDir\icon.ico"
if (-not (Test-Path $iconPath)) {
    Write-Host "  WARNING: icon.ico not found in installer/. The installer will fail." -ForegroundColor Red
    Write-Host "  Create a .ico file (256x256 recommended) and save as installer\icon.ico" -ForegroundColor Red
    throw "Missing icon.ico"
}

Write-Host "  All files present." -ForegroundColor Green

# ── Step 3: Find Inno Setup compiler ──
$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
)

$iscc = $null
foreach ($p in $isccPaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}

if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
}

Write-Host "  Found Inno Setup: $iscc" -ForegroundColor Green

# ── Step 4: Compile installer ──
Write-Host "`n[4/4] Compiling installer..." -ForegroundColor Yellow

# Ensure output folder exists (installer/Output/ - self-contained under installer/)
$outputDir = "$InstallerDir\Output"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Info: report whether any families are bundled this build
$familiesDir = "$InstallerDir\Families"
$rfaCount = 0
if (Test-Path $familiesDir) {
    $rfaCount = (Get-ChildItem -Path $familiesDir -Filter '*.rfa' -Recurse -ErrorAction SilentlyContinue).Count
}
if ($rfaCount -gt 0) {
    Write-Host "  Bundling $rfaCount .rfa families from installer\Families\" -ForegroundColor Green
} else {
    Write-Host "  No .rfa files in installer\Families\ - installer will ship with no families." -ForegroundColor DarkYellow
}

& $iscc "$InstallerDir\sg-revit-addin.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

# ── Optional: Sign the installer ──
if ($Sign) {
    if (-not $CertPath) { throw "Code signing requested but -CertPath not provided" }

    $installerExe = Get-ChildItem "$outputDir\SgRevitAddin-*-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $installerExe) { throw "Installer .exe not found in installer\Output\" }

    Write-Host "`n[Signing] Signing $($installerExe.Name)..." -ForegroundColor Yellow

    $signArgs = @(
        "sign",
        "/f", $CertPath,
        "/t", "http://timestamp.digicert.com",
        "/fd", "sha256"
    )

    if ($CertPassword) {
        $signArgs += @("/p", $CertPassword)
    }

    $signArgs += $installerExe.FullName

    & signtool.exe $signArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  WARNING: Signing failed. The installer will work but may trigger SmartScreen." -ForegroundColor Red
    } else {
        Write-Host "  Signed successfully." -ForegroundColor Green
    }
}

# ── Done ──
$output = Get-ChildItem "$outputDir\SgRevitAddin-*-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Installer built successfully!" -ForegroundColor Green
Write-Host "  Output: $($output.FullName)" -ForegroundColor Green
Write-Host "  Size:   $([math]::Round($output.Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

