[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputMsixN,

    [Parameter(Mandatory = $true)]
    [string]$InputMsixNPlus1,

    [Parameter(Mandatory = $true)]
    [string]$PackageIdentity,

    [Parameter(Mandatory = $true)]
    [string]$SeedRoot
)

$ErrorActionPreference = 'Stop'

foreach ($path in @($InputMsixN, $InputMsixNPlus1)) {
    if (-not (Test-Path $path)) { throw "MSIX not found: $path" }
}

function Get-MsixManifestInfo([string]$PackagePath) {
    $stage = Join-Path $env:RUNNER_TEMP ("butchi-upgrade-manifest-" + [guid]::NewGuid().ToString('N'))
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $PackagePath), $stage)
        [xml]$manifest = Get-Content (Join-Path $stage 'AppxManifest.xml') -Raw
        return [pscustomobject]@{
            Name = [string]$manifest.Package.Identity.Name
            Version = [version]([string]$manifest.Package.Identity.Version)
        }
    }
    finally {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    }
}

function Invoke-InstalledProbe($Package, [version]$ExpectedVersion, [string]$ProbePath) {
    $exe = Join-Path $Package.InstallLocation 'butchi.exe'
    if (-not (Test-Path $exe)) { throw "Installed executable missing: $exe" }

    $env:BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY = $PackageIdentity
    $env:BUTCHI_RELEASE_PROBE_PACKAGE_VERSION = $ExpectedVersion.ToString()
    $env:BUTCHI_RELEASE_PROBE_DATA_ROOT = $SeedRoot
    if (Test-Path $ProbePath) { Remove-Item $ProbePath -Force }

    $process = Start-Process -FilePath $exe -ArgumentList @('--release-probe', $ProbePath) -PassThru
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Upgrade release probe exceeded the 30 second timeout.'
    }
    if ($process.ExitCode -ne 0) { throw "Upgrade release probe exited with code $($process.ExitCode)." }
    if (-not (Test-Path $ProbePath)) { throw 'Upgrade release probe did not produce output JSON.' }

    $probe = Get-Content $ProbePath -Raw | ConvertFrom-Json
    if (-not $probe.success -or -not $probe.compositionHealthy) { throw 'Upgrade release probe reported unhealthy startup composition.' }
    if (-not $probe.configReadable) { throw 'Upgrade release probe could not read config data.' }
    if (-not $probe.historyReadable) { throw 'Upgrade release probe could not read history data.' }
    if ($probe.packageIdentity -ne $PackageIdentity) { throw 'Upgrade release probe package identity mismatch.' }
    if ([version]$probe.packageVersion -ne $ExpectedVersion) { throw 'Upgrade release probe package version mismatch.' }
    foreach ($sensitiveField in 'selectedText','promptContent','historyContent') {
        if ($null -ne $probe.$sensitiveField) { throw "Upgrade release probe emitted sensitive field content: $sensitiveField" }
    }
    return $probe
}

$n = Get-MsixManifestInfo $InputMsixN
$nPlus1 = Get-MsixManifestInfo $InputMsixNPlus1
if ($n.Name -ne $PackageIdentity -or $nPlus1.Name -ne $PackageIdentity) {
    throw "Upgrade packages must share identity '$PackageIdentity'."
}
if ($nPlus1.Version -le $n.Version) {
    throw "Upgrade version '$($nPlus1.Version)' must be greater than '$($n.Version)'."
}

$probeNPath = Join-Path $env:RUNNER_TEMP 'butchi-upgrade-probe-n.json'
$probeNPlus1Path = Join-Path $env:RUNNER_TEMP 'butchi-upgrade-probe-n-plus-1.json'

try {
    Get-AppxPackage -Name $PackageIdentity -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    if (Test-Path $SeedRoot) { Remove-Item $SeedRoot -Recurse -Force }
    New-Item -ItemType Directory -Force $SeedRoot | Out-Null
    Set-Content -Path (Join-Path $SeedRoot 'config.json') -Value '{}' -Encoding utf8

    Write-Host 'UPGRADE_N_INSTALL_BEGIN'
    Add-AppxPackage -Path (Resolve-Path $InputMsixN)
    Write-Host 'UPGRADE_N_INSTALL_END'

    $installedN = Get-AppxPackage -Name $PackageIdentity | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $installedN) { throw "Version N package registration not found: $PackageIdentity" }
    if ([version]$installedN.Version -ne $n.Version) { throw 'Installed version N does not match package N.' }

    $probeN = Invoke-InstalledProbe $installedN $n.Version $probeNPath

    Write-Host 'UPGRADE_N_PLUS_1_INSTALL_BEGIN'
    Add-AppxPackage -Path (Resolve-Path $InputMsixNPlus1)
    Write-Host 'UPGRADE_N_PLUS_1_INSTALL_END'

    $installedNPlus1 = Get-AppxPackage -Name $PackageIdentity | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $installedNPlus1) { throw "Version N+1 package registration not found: $PackageIdentity" }
    if ([version]$installedNPlus1.Version -ne $nPlus1.Version) { throw 'Installed version N+1 does not match package N+1.' }

    $probeNPlus1 = Invoke-InstalledProbe $installedNPlus1 $nPlus1.Version $probeNPlus1Path
    if ([int]$probeNPlus1.historyEntryCount -ne [int]$probeN.historyEntryCount) {
        throw 'History compatibility count changed across package upgrade.'
    }

    Write-Host "MSIX upgrade smoke passed: $($n.Version) -> $($nPlus1.Version)."
}
finally {
    Get-Process -Name 'butchi' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host 'UPGRADE_UNINSTALL_BEGIN'
    Get-AppxPackage -Name $PackageIdentity -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    Write-Host 'UPGRADE_UNINSTALL_END'
    if (Get-AppxPackage -Name $PackageIdentity -ErrorAction SilentlyContinue) {
        throw "Package registration remained after upgrade uninstall: $PackageIdentity"
    }
    Remove-Item Env:BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY -ErrorAction SilentlyContinue
    Remove-Item Env:BUTCHI_RELEASE_PROBE_PACKAGE_VERSION -ErrorAction SilentlyContinue
    Remove-Item Env:BUTCHI_RELEASE_PROBE_DATA_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $SeedRoot) { Remove-Item $SeedRoot -Recurse -Force }
    if (Test-Path $probeNPath) { Remove-Item $probeNPath -Force }
    if (Test-Path $probeNPlus1Path) { Remove-Item $probeNPlus1Path -Force }
}
