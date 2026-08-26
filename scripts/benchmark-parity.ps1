param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceCommand,

    [Parameter(Mandatory = $true)]
    [string]$CandidateCommand,

    [int]$Runs = 5,

    [string]$OutputPath = "artifacts/task12-validation/benchmark.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Runs -lt 5) {
    throw "Runs must be at least 5 for Task 12 parity validation."
}

$requiredMetrics = @(
    'selectionToPopover',
    'popoverToDispatch',
    'firstToken',
    'tokensPerSecond',
    'ram',
    'vram'
)

function Invoke-MetricCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    $raw = & pwsh -NoLogo -NoProfile -Command $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark command failed with exit code ${LASTEXITCODE}: $Command"
    }

    $text = ($raw | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Benchmark command produced no JSON output: $Command"
    }

    $metric = $text | ConvertFrom-Json
    foreach ($name in $requiredMetrics) {
        if ($null -eq $metric.$name) {
            throw "Benchmark JSON is missing '$name': $text"
        }
    }

    return [pscustomobject]@{
        selectionToPopover = [double]$metric.selectionToPopover
        popoverToDispatch = [double]$metric.popoverToDispatch
        firstToken = [double]$metric.firstToken
        tokensPerSecond = [double]$metric.tokensPerSecond
        ram = [double]$metric.ram
        vram = [double]$metric.vram

        # Compatibility aliases retained for the earlier Task 12 evidence contract.
        startup = [double]$metric.selectionToPopover
        workingSet = [double]$metric.ram
        inference = [double]$metric.firstToken
    }
}

function Get-Median {
    param([double[]]$Values)

    $ordered = @($Values | Sort-Object)
    $count = $ordered.Count
    if ($count -eq 0) { throw 'Cannot calculate median of an empty set.' }

    $middle = [int][Math]::Floor($count / 2)
    if ($count % 2 -eq 1) {
        return [double]$ordered[$middle]
    }

    return ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2.0
}

function Measure-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    $samples = for ($i = 0; $i -lt $Runs; $i++) {
        Invoke-MetricCommand -Command $Command
    }

    return [pscustomobject]@{
        runs = $Runs
        samples = @($samples)
        median = [pscustomobject]@{
            selectionToPopover = Get-Median @($samples.selectionToPopover)
            popoverToDispatch = Get-Median @($samples.popoverToDispatch)
            firstToken = Get-Median @($samples.firstToken)
            tokensPerSecond = Get-Median @($samples.tokensPerSecond)
            ram = Get-Median @($samples.ram)
            vram = Get-Median @($samples.vram)

            startup = Get-Median @($samples.startup)
            workingSet = Get-Median @($samples.workingSet)
            inference = Get-Median @($samples.inference)
        }
    }
}

$reference = Measure-Command -Command $ReferenceCommand
$candidate = Measure-Command -Command $CandidateCommand

$result = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    referenceCommand = $ReferenceCommand
    candidateCommand = $CandidateCommand
    reference = $reference
    candidate = $candidate
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding utf8
$result | ConvertTo-Json -Depth 8
