[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublicCertificatePath,

    [string]$Subject = 'CN=Butchi CI Test Signing'
)

$ErrorActionPreference = 'Stop'

$resolvedParent = Split-Path -Parent $PublicCertificatePath
if ([string]::IsNullOrWhiteSpace($resolvedParent)) {
    $resolvedParent = $PWD.Path
}
New-Item -ItemType Directory -Force $resolvedParent | Out-Null

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -NotAfter (Get-Date).AddDays(1)

if (-not $certificate.HasPrivateKey) {
    throw 'Ephemeral CI code-signing certificate does not have a private key.'
}

$publicPath = [IO.Path]::GetFullPath($PublicCertificatePath)
Export-Certificate -Cert $certificate -FilePath $publicPath -Force | Out-Null
if (-not (Test-Path $publicPath)) {
    throw "Public CI signing certificate was not exported: $publicPath"
}

[pscustomobject]@{
    Thumbprint = $certificate.Thumbprint
    PublicCertificatePath = $publicPath
    Subject = $certificate.Subject
    NotAfter = $certificate.NotAfter
}
