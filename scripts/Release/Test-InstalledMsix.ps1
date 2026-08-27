[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputMsix
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InputMsix)) { throw "MSIX not found: $InputMsix" }

if (-not ('Butchi.ReleaseValidation.ApplicationActivationManager' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Butchi.ReleaseValidation
{
    [Flags]
    public enum ActivateOptions : uint
    {
        None = 0
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, IntPtr verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    public class ApplicationActivationManager
    {
    }

    public static class ApplicationActivator
    {
        public static uint ActivateApplication(string appUserModelId, string arguments)
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                var result = manager.ActivateApplication(
                    appUserModelId,
                    arguments,
                    ActivateOptions.None,
                    out var processId);
                if (result < 0)
                    Marshal.ThrowExceptionForHR(result);
                return processId;
            }
            finally
            {
                if (Marshal.IsComObject(manager))
                    Marshal.FinalReleaseComObject(manager);
            }
        }
    }
}
'@
}

$stage = Join-Path $env:RUNNER_TEMP 'butchi-installed-msix-manifest'
$probePath = Join-Path $env:RUNNER_TEMP 'butchi-release-probe.json'
$identityName = $null

try {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $InputMsix), $stage)
    [xml]$manifest = Get-Content (Join-Path $stage 'AppxManifest.xml') -Raw
    $identityName = [string]$manifest.Package.Identity.Name
    $expectedVersion = [string]$manifest.Package.Identity.Version
    $applicationId = [string]$manifest.Package.Applications.Application.Id
    if ([string]::IsNullOrWhiteSpace($identityName)) { throw 'MSIX identity name is missing.' }
    if ([string]::IsNullOrWhiteSpace($expectedVersion)) { throw 'MSIX identity version is missing.' }
    if ([string]::IsNullOrWhiteSpace($applicationId)) { throw 'MSIX application id is missing.' }

    if ([string]::IsNullOrWhiteSpace($env:CI_SIGNING_CERT_PATH) -or -not (Test-Path $env:CI_SIGNING_CERT_PATH)) {
        throw 'CI signing public certificate is required for installed MSIX validation.'
    }

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

    if (Test-Path $probePath) { Remove-Item $probePath -Force }

    $appUserModelId = "$($package.PackageFamilyName)!$applicationId"
    $activationArguments = "--release-probe `"$probePath`""

    Write-Host 'PROBE_LAUNCH'
    $processId = [Butchi.ReleaseValidation.ApplicationActivator]::ActivateApplication(
        $appUserModelId,
        $activationArguments)
    if ($processId -eq 0) { throw 'Package activation did not return a process id.' }

    $process = [System.Diagnostics.Process]::GetProcessById([int]$processId)
    $probeProduced = $process.WaitForExit(30000)
    if (-not $probeProduced) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Installed release probe exceeded the 30 second timeout.'
    }
    Write-Host 'PROBE_EXIT'

    if (-not (Test-Path $probePath)) { throw 'Installed release probe did not produce output JSON.' }

    $probe = Get-Content $probePath -Raw | ConvertFrom-Json
    if (-not $probe.success -or -not $probe.compositionHealthy) { throw 'Installed release probe reported unhealthy startup composition.' }
    if (-not $probe.configReadable -or -not $probe.historyReadable) { throw 'Installed release probe could not read persisted app data.' }
    if (-not $probe.firstRunCompositionReady) { throw 'Installed release probe reported first-run composition not ready.' }
    if (-not $probe.trayReady) { throw 'Installed release probe reported tray startup not ready.' }
    if (-not $probe.settingsReady) { throw 'Installed release probe reported Settings not ready.' }
    if (-not $probe.modelsReady) { throw 'Installed release probe reported Models not ready.' }
    if (-not $probe.historyReady) { throw 'Installed release probe reported History not ready.' }
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
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $probePath) { Remove-Item $probePath -Force }
}

Write-Host "Installed MSIX smoke passed for $identityName."

$upgradeScript = Join-Path $PSScriptRoot 'Test-MsixUpgrade.ps1'
$upgradeSeedRoot = Join-Path $env:RUNNER_TEMP 'butchi-upgrade-seed'
& $upgradeScript `
    -InputMsixN (Resolve-Path $InputMsix) `
    -PackageIdentity $identityName `
    -SeedRoot $upgradeSeedRoot
