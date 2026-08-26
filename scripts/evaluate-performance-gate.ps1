param(
    [string]$BenchmarkPath = "artifacts/task12-validation/benchmark.json",
    [string]$JsonOutputPath = "artifacts/task12-validation/performance-summary.json",
    [string]$MarkdownOutputPath = "artifacts/task12-validation/performance-summary.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BenchmarkPath)) {
    throw "Task 12 benchmark evidence not found: $BenchmarkPath"
}

$benchmark = Get-Content -Raw -Path $BenchmarkPath | ConvertFrom-Json
$baseline = $benchmark.reference.median
$candidate = $benchmark.candidate.median

function Add-Result {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Metric,
        [double]$Baseline,
        [double]$Candidate,
        [string]$Rule,
        [bool]$Passed,
        [double]$Limit
    )

    $deltaPercent = if ($Baseline -eq 0) { 0.0 } else { (($Candidate - $Baseline) / $Baseline) * 100.0 }
    $Results.Add([pscustomobject]@{
        metric = $Metric
        baseline = [Math]::Round($Baseline, 4)
        candidate = [Math]::Round($Candidate, 4)
        deltaPercent = [Math]::Round($deltaPercent, 2)
        rule = $Rule
        limit = [Math]::Round($Limit, 4)
        passed = $Passed
    })
}

$results = [System.Collections.Generic.List[object]]::new()

$selectionToPopover = [double]$candidate.selectionToPopover
Add-Result $results 'selectionToPopover' ([double]$baseline.selectionToPopover) $selectionToPopover '< 50 ms' ($selectionToPopover -lt 50.0) 50.0

$popoverToDispatch = [double]$candidate.popoverToDispatch
Add-Result $results 'popoverToDispatch' ([double]$baseline.popoverToDispatch) $popoverToDispatch '< 30 ms' ($popoverToDispatch -lt 30.0) 30.0

$baselineFirstToken = [double]$baseline.firstToken
$candidateFirstToken = [double]$candidate.firstToken
$firstTokenLimit = $baselineFirstToken * 1.05
$baselineTotalPath = [double]$baseline.selectionToPopover + [double]$baseline.popoverToDispatch + $baselineFirstToken
$candidateTotalPath = $selectionToPopover + $popoverToDispatch + $candidateFirstToken
$totalPathImprovementLimit = $baselineTotalPath * 0.85
$firstTokenPassed = ($candidateFirstToken -le $firstTokenLimit) -or ($candidateTotalPath -le $totalPathImprovementLimit)
Add-Result $results 'firstToken' $baselineFirstToken $candidateFirstToken '<= 5% slower unless total path is >= 15% faster' $firstTokenPassed $firstTokenLimit

$baselineTokens = [double]$baseline.tokensPerSecond
$candidateTokens = [double]$candidate.tokensPerSecond
$tokensFloor = $baselineTokens * 0.95
Add-Result $results 'tokensPerSecond' $baselineTokens $candidateTokens '>= 95% of baseline' ($candidateTokens -ge $tokensFloor) $tokensFloor

$baselineRam = [double]$baseline.ram
$candidateRam = [double]$candidate.ram
$ramLimit = $baselineRam * 1.10
Add-Result $results 'ram' $baselineRam $candidateRam '<= 110% of baseline' ($candidateRam -le $ramLimit) $ramLimit

$baselineVram = [double]$baseline.vram
$candidateVram = [double]$candidate.vram
$vramLimit = $baselineVram * 1.10
Add-Result $results 'vram' $baselineVram $candidateVram '<= 110% of baseline' ($candidateVram -le $vramLimit) $vramLimit

$failed = @($results | Where-Object { -not $_.passed })
$summary = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    benchmarkPath = $BenchmarkPath
    passed = ($failed.Count -eq 0)
    failures = @($failed.metric)
    metrics = @($results)
}

$outputDirectory = Split-Path -Parent $JsonOutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonOutputPath -Encoding utf8

$lines = @(
    '# Task 12 Performance Gate',
    '',
    "Overall: **$(if ($summary.passed) { 'PASS' } else { 'FAIL' })**",
    '',
    '| Metric | Baseline | Candidate | Delta | Rule | Result |',
    '| --- | ---: | ---: | ---: | --- | --- |'
)
foreach ($result in $results) {
    $state = if ($result.passed) { 'PASS' } else { 'FAIL' }
    $lines += "| $($result.metric) | $($result.baseline) | $($result.candidate) | $($result.deltaPercent)% | $($result.rule) | $state |"
}
$lines | Set-Content -Path $MarkdownOutputPath -Encoding utf8

$summary | ConvertTo-Json -Depth 8

if (-not $summary.passed) {
    Write-Error "Task 12 performance gate failed: $($failed.metric -join ', ')"
    exit 1
}
