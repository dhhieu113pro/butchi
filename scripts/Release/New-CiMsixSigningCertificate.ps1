[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputCerPath,

    [Parameter(Mandatory = $true)]
    [string]$Subject
)

$ErrorActionPreference = 'Stop'
$cert = $null

try {
    $parent = Split-Path -Parent $OutputCerPath
    if ($parent) {
        New-Item -ItemType Directory -Force $parent | Out-Null
    }

    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddHours(4)

    Export-Certificate -Cert $cert -FilePath $OutputCerPath -Force | Out-Null
    Import-Certificate -FilePath $OutputCerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
    Import-Certificate -FilePath $OutputCerPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null

    Write-Output $cert.Thumbprint
}
catch {
    if ($cert) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\Root\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "Cert:\CurrentUser\TrustedPeople\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
    }
    if (Test-Path $OutputCerPath) {
        Remove-Item -LiteralPath $OutputCerPath -Force -ErrorAction SilentlyContinue
    }
    throw
}
