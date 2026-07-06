# Builds the custom SG installer.
#
#   .\build.ps1               # full build: self-contained single exe (payload embedded)
#   .\build.ps1 -SkipFamilies # fast UI-testing build: exe + sibling dist\payload\ (no families)
#   .\build.ps1 -NoBuild      # re-assemble without recompiling
#
# Full build output:  dist\SgRevitAddinSetup.exe  (one file, ~300 MB, carries the payload)
# Dev build output:   dist\SgRevitAddinSetup.exe  +  dist\payload\  (read at runtime)
param([switch]$SkipFamilies, [switch]$NoBuild)

$ErrorActionPreference = "Stop"
$root   = Split-Path -Parent $PSCommandPath           # installer-custom
$repo   = Split-Path -Parent $root                    # repo root
$dist   = Join-Path $root "dist"
$stage  = Join-Path $root "obj\payload-stage"         # under obj\ so default globs ignore it
$zip    = Join-Path $root "payload.zip"               # embedded by the csproj when present

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

# 1. Compile the add-in projects (the payload DLLs).
if (-not $NoBuild) {
  Write-Host "Building add-in projects (Release)..." -ForegroundColor Cyan
  dotnet build (Join-Path $repo "src\SgRevit24\SgRevit24.csproj") -c Release | Out-Null
  dotnet build (Join-Path $repo "src\SgRevit25\SgRevit25.csproj") -c Release | Out-Null
}

# 2. Assemble the payload staging tree (DLLs, manifests, families).
Write-Host "Assembling payload..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Tree (Join-Path $repo "src\SgRevit24\bin\Release") (Join-Path $stage "SgRevit24") @(".pdb", ".addin")
Copy-Tree (Join-Path $repo "src\SgRevit25\bin\Release") (Join-Path $stage "SgRevit25") @(".pdb", ".addin")
Copy-Item (Join-Path $repo "installer\SgRevit24.addin") (Join-Path $stage "SgRevit24.addin") -Force
Copy-Item (Join-Path $repo "installer\SgRevit25.addin") (Join-Path $stage "SgRevit25.addin") -Force

$famSrc = Join-Path $repo "installer\Families"
if (-not $SkipFamilies -and (Test-Path $famSrc)) {
  Write-Host "Copying family library (this is large)..." -ForegroundColor DarkGray
  Copy-Tree $famSrc (Join-Path $stage "Families") $null
} else {
  Write-Host "Skipping family library." -ForegroundColor DarkGray
}

# 3. Full build embeds the payload; dev build leaves it as a sibling folder.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item $zip -Force }
if (-not $SkipFamilies) {
  Write-Host "Packing payload.zip for embedding..." -ForegroundColor Cyan
  [System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

# 4. Build the installer exe. Clear the compile intermediates (but keep the staged
#    payload) so the embed state always matches whether payload.zip is present.
if (-not $NoBuild) {
  Write-Host "Building installer (Release)..." -ForegroundColor Cyan
  Remove-Item (Join-Path $root "bin") -Recurse -Force -ErrorAction SilentlyContinue
  Get-ChildItem (Join-Path $root "obj") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "payload-stage" } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  dotnet build (Join-Path $root "SgRevitAddinSetup.csproj") -c Release | Out-Null
}

# 5. Assemble dist\.
Write-Host "Assembling dist\..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $root "bin\Release\net48\SgRevitAddinSetup.exe") $dist -Force
if ($SkipFamilies) { Copy-Tree $stage (Join-Path $dist "payload") $null }   # sibling for local run

$exe  = Join-Path $dist "SgRevitAddinSetup.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
if ($SkipFamilies) {
  Write-Host "Dev build - payload is the sibling dist\payload folder." -ForegroundColor Green
} else {
  Write-Host "Self-contained single-exe installer (payload embedded)." -ForegroundColor Green
}
