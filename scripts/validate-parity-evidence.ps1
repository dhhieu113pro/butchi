param(
    [string]$EvidencePath = "artifacts/task12-validation/task12-parity-result.json",
    [string]$MarkdownOutputPath = "artifacts/task12-validation/parity-summary.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $EvidencePath)) {
    throw "Parity evidence file not found: $EvidencePath"
}

$evidence = Get-Content -Raw -Path $EvidencePath | ConvertFrom-Json
$architectures = @('x64', 'arm64')
$checks = @(
    'launch',
    'doubleCtrl',
    'selectionCapture',
    'clipboardPreserved',
    'translate',
    'rewrite',
    'cancel',
    'settings',
    'history',
    'models',
    'status',
    'modelLoading',
    'packagedLaunch'
)

$failures = [System.Collections.Generic.List[string]]::new()
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Task 12 Windows parity summary')
$lines.Add('')
$lines.Add('| Architecture | Check | Result |')
$lines.Add('| --- | --- | --- |')

foreach ($architecture in $architectures) {
    $archEvidence = $evidence.$architecture
    if ($null -eq $archEvidence) {
        $failures.Add("Missing architecture evidence: $architecture")
        foreach ($check in $checks) {
            $lines.Add("| $architecture | $check | MISSING |")
        }
        continue
    }

    foreach ($check in $checks) {
        $property = $archEvidence.PSObject.Properties[$check]
        $passed = $null -ne $property -and $property.Value -eq $true
        $status = if ($passed) { 'PASS' } else { 'FAIL' }
        $lines.Add("| $architecture | $check | $status |")
        if (-not $passed) {
            $failures.Add("$architecture/$check did not pass")
        }
    }
}

$directory = Split-Path -Parent $MarkdownOutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$lines | Set-Content -Path $MarkdownOutputPath -Encoding utf8

if ($failures.Count -gt 0) {
    Write-Error ("Task 12 parity evidence failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host 'Task 12 parity evidence passed for x64 and arm64.'
