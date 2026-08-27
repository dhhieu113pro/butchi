[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputMsix,

    [Parameter(Mandatory = $true)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $true)]
    [string]$ProductionRoot
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InputMsix)) {
    throw "CI MSIX input does not exist: $InputMsix"
}

$resolvedInput = [IO.Path]::GetFullPath((Resolve-Path $InputMsix).Path)
$resolvedProductionRoot = [IO.Path]::GetFullPath($ProductionRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$productionPrefix = $resolvedProductionRoot + [IO.Path]::DirectorySeparatorChar
if ($resolvedInput.Equals($resolvedProductionRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedInput.StartsWith($productionPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to sign production Store artifact path: $resolvedInput"
}

$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
if (-not $certificate.HasPrivateKey) {
    throw "CI signing certificate does not have a private key: $CertificateThumbprint"
}

$kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$signtool = Get-ChildItem (Join-Path $kitsRoot 'bin') -Filter 'signtool.exe' -Recurse |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signtool) {
    throw 'signtool.exe was not found in the Windows SDK.'
}

& $signtool.FullName sign /fd SHA256 /s My /sha1 $CertificateThumbprint $resolvedInput
if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE for $resolvedInput"
}

$signature = Get-AuthenticodeSignature -FilePath $resolvedInput
if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned -or -not $signature.SignerCertificate) {
    throw "CI MSIX signature was not present after signing: $resolvedInput"
}
if ($signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw "CI MSIX signer thumbprint '$($signature.SignerCertificate.Thumbprint)' does not match expected '$CertificateThumbprint'."
}

Write-Host "CI MSIX signed and verified with certificate $CertificateThumbprint: $resolvedInput"
