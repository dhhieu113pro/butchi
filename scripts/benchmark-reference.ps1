param(
    [Parameter(Mandatory = $true)]
    [string]$ApplicationPath,

    [Parameter(Mandatory = $true)]
    [string]$InferenceCommand,

    [int]$StartupTimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ApplicationPath)) {
    throw "Reference application not found: $ApplicationPath"
}

$startupWatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $ApplicationPath -PassThru
try {
    $ready = $process.WaitForInputIdle($StartupTimeoutSeconds * 1000)
    if (-not $ready) {
        throw "Reference application did not become input-idle within $StartupTimeoutSeconds seconds."
    }

    $startupWatch.Stop()
    $process.Refresh()
    $workingSetMb = [Math]::Round($process.WorkingSet64 / 1MB, 2)
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

$rawInference = & pwsh -NoLogo -NoProfile -Command $InferenceCommand
$inferenceExitCode = $LASTEXITCODE
if ($inferenceExitCode -ne 0) {
    throw "Reference inference command failed with exit code ${inferenceExitCode}."
}

$telemetryText = ($rawInference | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($telemetryText)) {
    throw 'Reference inference command produced no telemetry JSON.'
}

$telemetry = $telemetryText | ConvertFrom-Json
foreach ($name in @('popoverToDispatch', 'firstToken', 'tokensPerSecond', 'vram')) {
    if ($null -eq $telemetry.$name) {
        throw "Reference inference telemetry is missing '$name': $telemetryText"
    }
}

$ramMb = if ($null -ne $telemetry.ram) { [double]$telemetry.ram } else { $workingSetMb }
$selectionToPopoverMs = if ($null -ne $telemetry.selectionToPopover) {
    [double]$telemetry.selectionToPopover
} else {
    [Math]::Round($startupWatch.Elapsed.TotalMilliseconds, 2)
}

[pscustomobject]@{
    selectionToPopover = $selectionToPopoverMs
    popoverToDispatch = [double]$telemetry.popoverToDispatch
    firstToken = [double]$telemetry.firstToken
    tokensPerSecond = [double]$telemetry.tokensPerSecond
    ram = $ramMb
    vram = [double]$telemetry.vram
} | ConvertTo-Json -Compress
