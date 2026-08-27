[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputMsix
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InputMsix)) { throw "MSIX not found: $InputMsix" }

$stage = Join-Path $env:RUNNER_TEMP 'butchi-installed-msix-manifest'
$probePath = Join-Path $env:RUNNER_TEMP 'butchi-release-probe.json'
$identityName = $null
$rootThumbprint = $null

try {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $InputMsix), $stage)
    [xml]$manifest = Get-Content (Join-Path $stage 'AppxManifest.xml') -Raw
    $identityName = [string]$manifest.Package.Identity.Name
    $expectedVersion = [string]$manifest.Package.Identity.Version
    if ([string]::IsNullOrWhiteSpace($identityName)) { throw 'MSIX identity name is missing.' }
    if ([string]::IsNullOrWhiteSpace($expectedVersion)) { throw 'MSIX identity version is missing.' }

    if ([string]::IsNullOrWhiteSpace($env:CI_SIGNING_CERT_PATH) -or -not (Test-Path $env:CI_SIGNING_CERT_PATH)) {
        throw 'CI signing public certificate is required for installed MSIX validation.'
    }
    $rootCertificate = Import-Certificate -FilePath $env:CI_SIGNING_CERT_PATH -CertStoreLocation 'Cert:\CurrentUser\Root'
    $rootThumbprint = $rootCertificate.Thumbprint

    Write-Host 'INSTALL_BEGIN'
    Add-AppxPackage -Path (Resolve-Path $InputMsix)
    Write-Host 'INSTALL_END'

    $package = Get-AppxPackage -Name $identityName | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $package) { throw "Installed package registration not found: $identityName" }
    if ([string]$package.Version -ne $expectedVersion) { throw "Installed version '$($package.Version)' does not match '$expectedVersion'." }
    if ([string]::IsNullOrWhiteSpace($package.InstallLocation) -or -not (Test-Path $package.InstallLocation)) {
        throw 'Installed package location is missing.'
    }

    $exe = Join-Path $package.InstallLocation 'butchi.exe'
    if (-not (Test-Path $exe)) { throw "Installed executable missing: $exe" }

    $env:BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY = $identityName
    $env:BUTCHI_RELEASE_PROBE_PACKAGE_VERSION = $expectedVersion
    if (Test-Path $probePath) { Remove-Item $probePath -Force }

    Write-Host 'PROBE_LAUNCH'
    $process = Start-Process -FilePath $exe -ArgumentList @('--release-probe', $probePath) -PassThru
    $probeProduced = $process.WaitForExit(30000)
    if (-not $probeProduced) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Installed release probe exceeded the 30 second timeout.'
    }
    Write-Host 'PROBE_EXIT'

    if ($process.ExitCode -ne 0) { throw "Installed release probe exited with code $($process.ExitCode)." }
    if (-not (Test-Path $probePath)) { throw 'Installed release probe did not produce output JSON.' }

    $probe = Get-Content $probePath -Raw | ConvertFrom-Json
    if (-not $probe.success -or -not $probe.compositionHealthy) { throw 'Installed release probe reported unhealthy startup composition.' }
    if ($probe.packageIdentity -ne $identityName) { throw 'Release probe package identity mismatch.' }
    if ($probe.packageVersion -ne $expectedVersion) { throw 'Release probe package version mismatch.' }
    foreach ($sensitiveField in 'selectedText','promptContent','historyContent') {
        if ($null -ne $probe.$sensitiveField) { throw "Release probe emitted sensitive field content: $sensitiveField" }
    }
}
finally {
    Get-Process -Name 'butchi' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($identityName) {
        Write-Host 'UNINSTALL_BEGIN'
        Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
        Write-Host 'UNINSTALL_END'
        if (Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue) { throw "Package registration remained after uninstall: $identityName" }
    }
    if ($rootThumbprint) {
        Remove-Item "Cert:\CurrentUser\Root\$rootThumbprint" -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY -ErrorAction SilentlyContinue
    Remove-Item Env:BUTCHI_RELEASE_PROBE_PACKAGE_VERSION -ErrorAction SilentlyContinue
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $probePath) { Remove-Item $probePath -Force }
}

Write-Host "Installed MSIX smoke passed for $identityName."
