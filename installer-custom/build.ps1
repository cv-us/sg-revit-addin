# Builds the custom SG installer and assembles a runnable dist\ folder:
#   dist\SgRevitAddinSetup.exe   + dist\payload\  (DLLs, manifests, families)
#
#   .\build.ps1               # full build incl. families (large copy)
#   .\build.ps1 -SkipFamilies # fast UI-testing build (no family library)
param([switch]$SkipFamilies, [switch]$NoBuild)

$ErrorActionPreference = "Stop"
$root   = Split-Path -Parent $PSCommandPath           # installer-custom
$repo   = Split-Path -Parent $root                    # repo root
$dist   = Join-Path $root "dist"
$payload= Join-Path $dist "payload"

function Copy-Tree($src, $dst, [string[]]$excludeExt) {
  New-Item -ItemType Directory -Force -Path $dst | Out-Null
  Get-ChildItem -Path $src -Recurse -File | ForEach-Object {
    if ($excludeExt -and ($excludeExt -contains $_.Extension.ToLower())) { return }
    $rel = $_.FullName.Substring($src.Length).TrimStart('\')
    $target = Join-Path $dst $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item $_.FullName $target -Force
  }
}

if (-not $NoBuild) {
  Write-Host "Building add-in projects (Release)..." -ForegroundColor Cyan
  dotnet build (Join-Path $repo "src\SgRevit24\SgRevit24.csproj") -c Release | Out-Null
  dotnet build (Join-Path $repo "src\SgRevit25\SgRevit25.csproj") -c Release | Out-Null
  Write-Host "Building installer (Release)..." -ForegroundColor Cyan
  dotnet build (Join-Path $root "SgRevitAddinSetup.csproj") -c Release | Out-Null
}

Write-Host "Assembling dist\..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# Installer exe
Copy-Item (Join-Path $root "bin\Release\net48\SgRevitAddinSetup.exe") $dist -Force

# Add-in payloads (bin\Release minus pdb + the .addin manifest, which goes to the parent)
Copy-Tree (Join-Path $repo "src\SgRevit24\bin\Release") (Join-Path $payload "SgRevit24") @(".pdb", ".addin")
Copy-Tree (Join-Path $repo "src\SgRevit25\bin\Release") (Join-Path $payload "SgRevit25") @(".pdb", ".addin")

# Manifests
Copy-Item (Join-Path $repo "installer\SgRevit24.addin") (Join-Path $payload "SgRevit24.addin") -Force
Copy-Item (Join-Path $repo "installer\SgRevit25.addin") (Join-Path $payload "SgRevit25.addin") -Force

# Families
$famSrc = Join-Path $repo "installer\Families"
if (-not $SkipFamilies -and (Test-Path $famSrc)) {
  Write-Host "Copying family library (this is large)..." -ForegroundColor DarkGray
  Copy-Tree $famSrc (Join-Path $payload "Families") $null
} else {
  Write-Host "Skipping family library." -ForegroundColor DarkGray
}

$exe = Join-Path $dist "SgRevitAddinSetup.exe"
Write-Host "Done: $exe" -ForegroundColor Green
Write-Host "Run it (elevates for install). Payload is in dist\payload\." -ForegroundColor Green
