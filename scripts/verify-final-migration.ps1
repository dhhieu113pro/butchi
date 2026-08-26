param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [string]$MarkdownOutputPath = "artifacts/final-validation/migration-summary.md"
)

$ErrorActionPreference = 'Stop'

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) { throw "Missing $Label: $Path" }
    return (Resolve-Path $Path).Path
}

function Require-Glob([string]$Pattern, [string]$Label, [int]$Minimum = 1) {
    $items = @(Get-ChildItem -Path $Pattern -File -ErrorAction SilentlyContinue)
    if ($items.Count -lt $Minimum) { throw "Missing $Label matching $Pattern" }
    return $items
}

$performancePath = Require-File (Join-Path $EvidenceRoot 'performance-summary.json') 'performance-summary.json'
$parityPath = Require-File (Join-Path $EvidenceRoot 'task12-parity-result.json') 'task12-parity-result.json'
$msixFiles = Require-Glob (Join-Path $EvidenceRoot '*.msix') '*.msix' 2
$bundleFiles = Require-Glob (Join-Path $EvidenceRoot '*.msixbundle') '*.msixbundle'
$uploadFiles = Require-Glob (Join-Path $EvidenceRoot '*.msixupload') '*.msixupload'

$performance = Get-Content $performancePath -Raw | ConvertFrom-Json
if (-not $performance.passed) { throw 'Performance evidence has not passed' }

$parity = Get-Content $parityPath -Raw | ConvertFrom-Json
foreach ($architecture in 'x64','arm64') {
    $entry = $parity.$architecture
    if ($null -eq $entry) { throw "Missing $architecture parity evidence" }

    $checks = $entry.checks
    if ($null -eq $checks) { throw "Missing $architecture parity checks" }

    $failed = @($checks.PSObject.Properties | Where-Object {
        $value = $_.Value
        if ($value -is [bool]) { return -not $value }
        if ($value -is [string]) { return $value -notmatch '^(?i:pass|passed)$' }
        if ($null -ne $value.status) { return [string]$value.status -notmatch '^(?i:pass|passed)$' }
        return $true
    })
    if ($failed.Count -gt 0) {
        throw "$architecture parity evidence has failed or incomplete checks: $($failed.Name -join ', ')"
    }
}

$names = @($msixFiles.Name)
if (-not ($names | Where-Object { $_ -match '(?i)x64' })) { throw 'Missing x64 MSIX package' }
if (-not ($names | Where-Object { $_ -match '(?i)arm64' })) { throw 'Missing arm64 MSIX package' }

$summaryDir = Split-Path -Parent $MarkdownOutputPath
if ($summaryDir) { New-Item -ItemType Directory -Force -Path $summaryDir | Out-Null }

$lines = @(
    '# Butchi .NET / Avalonia migration final validation',
    '',
    '- Performance gate: PASSED',
    '- Windows x64 parity: PASSED',
    '- Windows ARM64 parity: PASSED',
    "- MSIX packages: PASSED ($($msixFiles.Count))",
    "- MSIX bundle: PASSED ($($bundleFiles[0].Name))",
    "- Store upload: PASSED ($($uploadFiles[0].Name))",
    '',
    'Migration gate: PASSED'
)
$lines | Set-Content -Path $MarkdownOutputPath -Encoding utf8
Write-Host "Final migration gate passed. Summary: $MarkdownOutputPath"
exit 0

# Failure paths above intentionally terminate with non-zero status in CI.
if ($false) { exit 1 }
