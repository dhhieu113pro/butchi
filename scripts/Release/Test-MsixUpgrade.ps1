[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputMsixN,

    [string]$InputMsixNPlus1,

    [Parameter(Mandatory = $true)]
    [string]$PackageIdentity,

    [Parameter(Mandatory = $true)]
    [string]$SeedRoot
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InputMsixN)) { throw "MSIX not found: $InputMsixN" }
if ($InputMsixNPlus1 -and -not (Test-Path $InputMsixNPlus1)) { throw "MSIX not found: $InputMsixNPlus1" }

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

function New-UpgradePackage([string]$PackagePath, [version]$SourceVersion) {
    if ([string]::IsNullOrWhiteSpace($env:CI_SIGNING_THUMBPRINT)) {
        throw 'CI_SIGNING_THUMBPRINT is required to build the N+1 upgrade package.'
    }
    if ($SourceVersion.Revision -ge 65535) { throw 'Cannot increment MSIX revision beyond 65535.' }

    $kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $makeAppx = Get-ChildItem (Join-Path $kitsRoot 'bin') -Filter 'MakeAppx.exe' -Recurse |
        Where-Object { $_.FullName -match '\\x64\\MakeAppx\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $makeAppx) { throw 'MakeAppx.exe was not found in the Windows SDK.' }

    $stage = Join-Path $env:RUNNER_TEMP 'butchi-upgrade-n-plus-1-stage'
    $output = Join-Path $env:RUNNER_TEMP 'Butchi_CI_upgrade_n_plus_1.msix'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $output) { Remove-Item $output -Force }

    & $makeAppx.FullName unpack /p (Resolve-Path $PackagePath) /d $stage /o
    if ($LASTEXITCODE -ne 0) { throw 'Failed to unpack version N package.' }

    foreach ($metadata in 'AppxSignature.p7x','AppxBlockMap.xml','[Content_Types].xml') {
        $metadataPath = Join-Path $stage $metadata
        if (Test-Path -LiteralPath $metadataPath) { Remove-Item -LiteralPath $metadataPath -Force }
    }

    $manifestPath = Join-Path $stage 'AppxManifest.xml'
    [xml]$manifest = Get-Content $manifestPath -Raw
    $nextVersion = [version]::new($SourceVersion.Major, $SourceVersion.Minor, $SourceVersion.Build, $SourceVersion.Revision + 1)
    $manifest.Package.Identity.Version = $nextVersion.ToString()
    $manifest.Save($manifestPath)

    & $makeAppx.FullName pack /d $stage /p $output /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $output)) { throw 'Failed to pack version N+1 package.' }

    ./scripts/Release/Sign-CiMsix.ps1 `
        -InputMsix $output `
        -CertificateThumbprint $env:CI_SIGNING_THUMBPRINT `
        -ProductionRoot (Join-Path $PWD 'artifacts/production-msix')

    return $output
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
if ($n.Name -ne $PackageIdentity) { throw "Version N package identity '$($n.Name)' does not match '$PackageIdentity'." }

$generatedNPlus1 = $false
if ([string]::IsNullOrWhiteSpace($InputMsixNPlus1)) {
    $InputMsixNPlus1 = New-UpgradePackage $InputMsixN $n.Version
    $generatedNPlus1 = $true
}

$nPlus1 = Get-MsixManifestInfo $InputMsixNPlus1
if ($nPlus1.Name -ne $PackageIdentity) { throw "Version N+1 package identity '$($nPlus1.Name)' does not match '$PackageIdentity'." }
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
    if ($generatedNPlus1 -and $InputMsixNPlus1 -and (Test-Path $InputMsixNPlus1)) { Remove-Item $InputMsixNPlus1 -Force }
    $generatedStage = Join-Path $env:RUNNER_TEMP 'butchi-upgrade-n-plus-1-stage'
    if (Test-Path $generatedStage) { Remove-Item $generatedStage -Recurse -Force }
}
