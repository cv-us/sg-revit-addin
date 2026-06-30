# SG Revit Addin - Trust the "Lee" code-signing certificate on this PC
#
# Run this ONCE PER OFFICE MACHINE, as Administrator. It imports the PUBLIC
# certificate into the machine's Trusted Root + Trusted Publishers stores so a
# signed SG installer shows "Lee" as a verified publisher (no "unknown
# publisher" prompt) on this machine.
#
# It does NOT contain any private key — it cannot be used to sign anything.
#
# Usage (elevated PowerShell):
#   powershell -ExecutionPolicy Bypass -File trust-codesign-cert.ps1
#   powershell -ExecutionPolicy Bypass -File trust-codesign-cert.ps1 -CerPath "\\share\Lee-CodeSign.cer"

param(
    [string]$CerPath = "$PSScriptRoot\..\installer\codesign\Lee-CodeSign.cer"
)

$ErrorActionPreference = "Stop"

# Require elevation (LocalMachine stores need admin).
$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) { throw "Run this in an elevated (Administrator) PowerShell window." }

if (-not (Test-Path $CerPath)) { throw "Certificate not found: $CerPath" }

Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null

Write-Output "Trusted '$CerPath' on this machine (Trusted Root + Trusted Publishers)."
Write-Output "Signed SG installers will now show their verified publisher here."
