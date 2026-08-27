[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StagePath,

    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$PackagePath,
    [string]$BundlePath,
    [string]$UploadPath
)

$ErrorActionPreference = 'Stop'

function Assert-Exists([string]$Path, [string]$Description) {
    if (-not (Test-Path $Path)) {
        throw "$Description missing: $Path"
    }
}

function Assert-Unsigned([string]$Path) {
    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
        throw "Production Store artifact must remain unsigned: $Path (signature status: $($signature.Status))"
    }
}

$versionParts = @($Version.Split('.'))
if ($versionParts.Count -ne 4) {
    throw "MSIX version must contain exactly four numeric components: $Version"
}
foreach ($part in $versionParts) {
    $parsed = 0
    if (-not [int]::TryParse($part, [ref]$parsed) -or $parsed -lt 0 -or $parsed -gt 65535) {
        throw "Invalid MSIX version component '$part' in $Version"
    }
}

Assert-Exists $StagePath 'Store package stage directory'
$manifestPath = Join-Path $StagePath 'Package.appxmanifest'
Assert-Exists $manifestPath 'Package.appxmanifest'
Assert-Exists (Join-Path $StagePath 'butchi.exe') 'butchi.exe'

foreach ($asset in 'StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png') {
    Assert-Exists (Join-Path $StagePath "Assets/$asset") $asset
}

[xml]$manifest = Get-Content $manifestPath -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')

$identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $ns)
if (-not $identity) { throw 'Store manifest Identity element is missing.' }
if ($identity.ProcessorArchitecture -ne $Architecture) {
    throw "Store manifest architecture '$($identity.ProcessorArchitecture)' does not match expected '$Architecture'."
}
if ($identity.Version -ne $Version) {
    throw "Store manifest version '$($identity.Version)' does not match expected '$Version'."
}

$application = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $ns)
if (-not $application) { throw 'Store manifest Application element is missing.' }
if ($application.EntryPoint -ne 'Windows.FullTrustApplication') {
    throw "Store manifest must use Windows.FullTrustApplication entry point."
}
if ($application.Executable -ne 'butchi.exe') {
    throw "Store manifest executable must be butchi.exe."
}

if ($PackagePath) {
    Assert-Exists $PackagePath 'Architecture-specific MSIX package'
    if ([IO.Path]::GetExtension($PackagePath) -ne '.msix') {
        throw "Architecture-specific Store package must use .msix extension: $PackagePath"
    }
    Assert-Unsigned $PackagePath
}

if ($BundlePath) {
    Assert-Exists $BundlePath 'MSIX bundle'
    $bundle = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $BundlePath))
    try {
        $packages = @($bundle.Entries | Where-Object { $_.FullName -match '\.msix$' })
        if ($packages.Count -ne 2) {
            throw "MSIX bundle must contain exactly two architecture packages; found $($packages.Count)."
        }
    }
    finally {
        $bundle.Dispose()
    }
}

if ($UploadPath) {
    Assert-Exists $UploadPath 'Store .msixupload container'
    $upload = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $UploadPath))
    try {
        $bundles = @($upload.Entries | Where-Object { $_.FullName -match '\.msixbundle$' })
        if ($bundles.Count -ne 1) {
            throw "Store .msixupload must contain exactly one .msixbundle; found $($bundles.Count)."
        }
    }
    finally {
        $upload.Dispose()
    }
}

Write-Host "Store package validation passed for $Architecture $Version."
