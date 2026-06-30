# SG Revit Addin - Sign a release artifact with the "Lee" code-signing cert
#
# Authenticode-signs a file (default: the newest installer in installer/Output)
# using the self-signed cert created by make-codesign-cert.ps1. Uses SHA-256 and
# an RFC-3161 timestamp so the signature stays valid after the cert expires.
#
# Run AFTER compiling the installer (ISCC) and BEFORE uploading the release.
#
# Usage:
#   powershell -File tools/sign-release.ps1
#   powershell -File tools/sign-release.ps1 -Path "installer\Output\SgRevitAddin-0.3.8-Setup.exe"

param(
    [string]$Path,
    [string]$SubjectName = "Lee",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

# Default to the newest Setup.exe in installer/Output.
if (-not $Path) {
    $newest = Get-ChildItem "$PSScriptRoot\..\installer\Output\SgRevitAddin-*-Setup.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "No installer found in installer\Output and no -Path given." }
    $Path = $newest.FullName
}
if (-not (Test-Path $Path)) { throw "File not found: $Path" }

# Find the signing cert by subject in the current user's store.
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq "CN=$SubjectName" } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    throw "No code-signing cert 'CN=$SubjectName' found. Run tools/make-codesign-cert.ps1 first."
}

Write-Output "Signing: $Path"
Write-Output "  with  : $($cert.Subject)  [$($cert.Thumbprint)]"

$result = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert `
    -HashAlgorithm SHA256 -TimestampServer $TimestampUrl

Write-Output ""
Write-Output "  Status        : $($result.Status)"
Write-Output "  StatusMessage : $($result.StatusMessage)"
if ($result.TimeStamperCertificate) { Write-Output "  Timestamped   : yes" } else { Write-Output "  Timestamped   : NO (timestamp server unreachable?)" }

if ($result.Status -ne 'Valid' -and $result.Status -ne 'UnknownError') {
    throw "Signing failed: $($result.Status) - $($result.StatusMessage)"
}
# Note: on a machine that hasn't trusted the cert yet, Status reports
# 'UnknownError' (untrusted chain) even though the signature IS applied.
Write-Output ""
Write-Output "Done. Publisher will show as '$SubjectName' on machines that trust the cert."
