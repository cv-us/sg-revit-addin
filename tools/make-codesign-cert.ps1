# SG Revit Addin - Create the self-signed code-signing certificate
#
# Creates a self-signed Authenticode code-signing certificate in the CURRENT
# USER's personal store and exports its PUBLIC half (.cer) so it can be trusted
# on the office machines (see trust-codesign-cert.ps1).
#
# The PRIVATE key stays in this user's store and is NEVER written to the repo.
# Run this ONCE on the machine that cuts releases (Lee's PC). Signing then uses
# sign-release.ps1, which finds this cert by subject name.
#
# Usage:
#   powershell -File tools/make-codesign-cert.ps1                 # subject "Lee", 15 yr
#   powershell -File tools/make-codesign-cert.ps1 -SubjectName "Lee" -Years 20 -Force

param(
    [string]$SubjectName = "Lee",
    [int]$Years = 15,
    [string]$OutCer = "$PSScriptRoot\..\installer\codesign\Lee-CodeSign.cer",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$subject = "CN=$SubjectName"

# Reuse an existing cert with this subject unless -Force.
$existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $subject } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if ($existing -and -not $Force) {
    Write-Output "Reusing existing code-signing cert for '$subject':"
    $cert = $existing
} else {
    Write-Output "Creating self-signed code-signing cert for '$subject' ($Years yr)..."
    $cert = New-SelfSignedCertificate `
        -Subject $subject `
        -Type CodeSigningCert `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears($Years)
}

# Export the PUBLIC certificate (.cer) for distribution / trust.
$outDir = Split-Path -Parent $OutCer
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
Export-Certificate -Cert $cert -FilePath $OutCer -Type CERT | Out-Null

Write-Output ""
Write-Output "  Subject     : $($cert.Subject)"
Write-Output "  Thumbprint  : $($cert.Thumbprint)"
Write-Output "  Expires     : $($cert.NotAfter)"
Write-Output "  Public .cer : $OutCer"
Write-Output ""
Write-Output "Next:"
Write-Output "  1. Sign an installer:  powershell -File tools/sign-release.ps1"
Write-Output "  2. Trust on each PC :  (admin) powershell -File tools/trust-codesign-cert.ps1"
