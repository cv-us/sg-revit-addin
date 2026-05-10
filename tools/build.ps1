# SG Revit Addin - Build Script
# Usage: .\tools\build.ps1 [-Configuration Debug|Release] [-Project SgRevit24|SgRevit25|All]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("SgRevit24", "SgRevit25", "All")]
    [string]$Project = "All"
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building SG Revit Addin ($Configuration)..." -ForegroundColor Cyan

if ($Project -eq "All" -or $Project -eq "SgRevit24") {
    Write-Host "`nBuilding SgRevit24 (.NET Framework 4.8)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SgRevit24\SgRevit24.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "SgRevit24 build failed" }
}

if ($Project -eq "All" -or $Project -eq "SgRevit25") {
    Write-Host "`nBuilding SgRevit25 (.NET 8)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SgRevit25\SgRevit25.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "SgRevit25 build failed" }
}

Write-Host "`nBuild complete." -ForegroundColor Green

