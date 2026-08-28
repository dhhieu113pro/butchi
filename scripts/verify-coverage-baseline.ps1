param(
    [string]$CoverageRoot = "TestResults",
    [double]$MinimumLineCoverage = 39.0
)

$ErrorActionPreference = "Stop"

$coverageFiles = @(Get-ChildItem -Path $CoverageRoot -Filter "coverage.cobertura.xml" -Recurse -File)
if ($coverageFiles.Count -eq 0) {
    Write-Error "No coverage.cobertura.xml files found under '$CoverageRoot'."
    exit 1
}

$lines = @{}
foreach ($coverageFile in $coverageFiles) {
    [xml]$coverage = Get-Content -Path $coverageFile.FullName -Raw

    foreach ($package in @($coverage.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            foreach ($line in @($class.lines.line)) {
                $key = "$($class.filename)|$($line.number)"
                $hits = [int]$line.hits

                if (-not $lines.ContainsKey($key) -or $hits -gt [int]$lines[$key]) {
                    $lines[$key] = $hits
                }
            }
        }
    }
}

$totalLines = $lines.Count
if ($totalLines -eq 0) {
    Write-Error "Coverage reports contained no source lines."
    exit 1
}

$coveredLines = @($lines.GetEnumerator() | Where-Object { [int]$_.Value -gt 0 }).Count
$lineCoverage = [Math]::Round(($coveredLines * 100.0) / $totalLines, 2)

$summaryDirectory = Join-Path $PSScriptRoot "..\artifacts\coverage"
New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
$summaryPath = Join-Path $summaryDirectory "coverage-summary.json"

[ordered]@{
    lineCoverage = $lineCoverage
    minimumLineCoverage = $MinimumLineCoverage
    coveredLines = $coveredLines
    totalLines = $totalLines
    reportCount = $coverageFiles.Count
} | ConvertTo-Json | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "Line coverage: $lineCoverage% ($coveredLines / $totalLines); required: $MinimumLineCoverage%."

if ($lineCoverage -lt $MinimumLineCoverage) {
    Write-Error "Line coverage $lineCoverage% is below the required baseline of $MinimumLineCoverage%."
    exit 1
}
