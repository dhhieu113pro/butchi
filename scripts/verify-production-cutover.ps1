param([string]$RepositoryRoot = (Resolve-Path "$PSScriptRoot/..").Path)

$ErrorActionPreference = 'Stop'

$readme = Get-Content (Join-Path $RepositoryRoot 'README.md') -Raw
if ($readme -match '(?i)successor implementation|butchi-fake') {
    throw 'README still identifies the production implementation as a migration/successor repository.'
}

$workflowPaths = @(
    '.github/workflows/ci.yml',
    '.github/workflows/parity.yml',
    '.github/workflows/final-validation.yml',
    '.github/workflows/release.yml'
)

foreach ($relative in $workflowPaths) {
    $path = Join-Path $RepositoryRoot $relative
    if (-not (Test-Path $path)) {
        throw "Missing canonical workflow: $relative"
    }

    $text = Get-Content $path -Raw
    if ($text -match '(?i)github\.com/dhhieu113pro/butchi-fake/(main|master)|raw\.githubusercontent\.com/dhhieu113pro/butchi-fake/(main|master)') {
        throw "Mutable butchi-fake dependency remains in $relative"
    }
}

Write-Host 'Production cutover repository contract passed.'
