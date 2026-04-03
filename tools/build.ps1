# SSG FP Suite - Build Script
# Usage: .\tools\build.ps1 [-Configuration Debug|Release] [-Project SSG24|SSG25|All]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("SSG24", "SSG25", "All")]
    [string]$Project = "All"
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building SSG FP Suite ($Configuration)..." -ForegroundColor Cyan

if ($Project -eq "All" -or $Project -eq "SSG24") {
    Write-Host "`nBuilding SSG24 (.NET Framework 4.8)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SSG24\SSG24.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "SSG24 build failed" }
}

if ($Project -eq "All" -or $Project -eq "SSG25") {
    Write-Host "`nBuilding SSG25 (.NET 8)..." -ForegroundColor Yellow
    dotnet build "$SolutionRoot\src\SSG25\SSG25.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "SSG25 build failed" }
}

Write-Host "`nBuild complete." -ForegroundColor Green
