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

$inputPath = (Resolve-Path $InputMsix).Path
$productionPath = (Resolve-Path $ProductionRoot).Path.TrimEnd('\', '/')
$productionPrefix = $productionPath + [IO.Path]::DirectorySeparatorChar

if ($inputPath.Equals($productionPath, [StringComparison]::OrdinalIgnoreCase) -or
    $inputPath.StartsWith($productionPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to sign production Store artifact path: $inputPath"
}

$cert = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
if (-not $cert.HasPrivateKey) {
    throw "CI signing certificate does not have a private key: $CertificateThumbprint"
}

$kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$SignTool = Get-ChildItem (Join-Path $kitsRoot 'bin') -Filter 'signtool.exe' -Recurse |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $SignTool) {
    throw 'SignTool.exe was not found in the Windows SDK.'
}

& $SignTool.FullName sign /sha1 $CertificateThumbprint /fd SHA256 /v $inputPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed for CI MSIX copy: $inputPath"
}

& $SignTool.FullName verify /pa /v $inputPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool verify failed for CI MSIX copy: $inputPath"
}

$signature = Get-AuthenticodeSignature -FilePath $inputPath
if (-not $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw 'CI MSIX signer thumbprint does not match the ephemeral certificate.'
}

Write-Host "CI-only MSIX signature validated: $inputPath"
