# SG Revit Addin - Deploy Add-in Script
# Usage: .\tools\deploy-addin.ps1 -RevitVersion 2023|2024|2025|2026 [-Configuration Debug|Release]
#
# SgRevit24.dll works in Revit 2023 and 2024 (both use .NET Framework 4.8)
# SgRevit25.dll works in Revit 2025 and 2026 (both use .NET 8)

param(
    [Parameter(Mandatory)]
    [ValidateSet("2023", "2024", "2025", "2026")]
    [string]$RevitVersion,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSScriptRoot

# Map Revit version to project
# Revit 2023 and 2024 → SgRevit24 (.NET Framework 4.8)
# Revit 2025 and 2026 → SgRevit25 (.NET 8)
$ProjectMap = @{
    "2023" = @{ Name = "SgRevit24" }
    "2024" = @{ Name = "SgRevit24" }
    "2025" = @{ Name = "SgRevit25" }
    "2026" = @{ Name = "SgRevit25" }
}

$project = $ProjectMap[$RevitVersion]
$projectName = $project.Name

# Paths
$buildOutput = "$SolutionRoot\src\$projectName\bin\$Configuration"
$addinSource = "$SolutionRoot\src\$projectName\$projectName.addin"
$revitAddinsFolder = "$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"

# Ensure Revit Addins folder exists
if (-not (Test-Path $revitAddinsFolder)) {
    New-Item -ItemType Directory -Path $revitAddinsFolder -Force | Out-Null
}

# Copy DLL
Write-Host "Deploying $projectName to Revit $RevitVersion..." -ForegroundColor Cyan

$dllSource = "$buildOutput\$projectName.dll"
if (-not (Test-Path $dllSource)) {
    throw "Build output not found at $dllSource. Run build.ps1 first."
}

Copy-Item $dllSource $revitAddinsFolder -Force
Write-Host "  Copied $projectName.dll" -ForegroundColor Gray

# Copy dependency DLLs (SgRevit24 needs System.Text.Json and transitive deps)
$depDlls = Get-ChildItem "$buildOutput\*.dll" -Exclude "$projectName.dll"
foreach ($dep in $depDlls) {
    Copy-Item $dep.FullName $revitAddinsFolder -Force
    Write-Host "  Copied $($dep.Name)" -ForegroundColor Gray
}

# Copy .deps.json if present (SgRevit25 needs this)
$depsJson = "$buildOutput\$projectName.deps.json"
if (Test-Path $depsJson) {
    Copy-Item $depsJson $revitAddinsFolder -Force
    Write-Host "  Copied $projectName.deps.json" -ForegroundColor Gray
}

# Copy .addin manifest (rename to match Revit version for clarity)
Copy-Item $addinSource $revitAddinsFolder -Force
Write-Host "  Copied $projectName.addin" -ForegroundColor Gray

# Copy defaults.json if present
$defaultsSource = "$buildOutput\Config\defaults.json"
if (Test-Path $defaultsSource) {
    $configDir = "$revitAddinsFolder\Config"
    if (-not (Test-Path $configDir)) { New-Item -ItemType Directory -Path $configDir -Force | Out-Null }
    Copy-Item $defaultsSource $configDir -Force
    Write-Host "  Copied Config\defaults.json" -ForegroundColor Gray
}

Write-Host "`nDeploy complete. Restart Revit to load the add-in." -ForegroundColor Green
Write-Host "Look for the 'SG' tab in the ribbon." -ForegroundColor Gray

