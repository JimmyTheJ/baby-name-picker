#!/usr/bin/env pwsh
# Run LLM name enrichment inside the baby-name-picker Docker container.
# Requires: container running (docker compose up -d)
#
# Usage:
#   .\enrich-names.ps1
#   .\enrich-names.ps1 --batch 50
#   .\enrich-names.ps1 --batch 50 --force
#   .\enrich-names.ps1 --provider OpenAI --batch 25
#   .\enrich-names.ps1 --all --batch 50    # repeat until no names left

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$all = $false
$forwardArgs = [System.Collections.Generic.List[string]]::new()
foreach ($arg in $args) {
    if ($arg -eq "--all") {
        $all = $true
    } else {
        $forwardArgs.Add([string]$arg)
    }
}

$running = docker compose ps --status running --services 2>$null
if (-not ($running | Where-Object { $_ -eq "baby-name-picker" })) {
    throw "baby-name-picker is not running. Start it with: docker compose up -d"
}

function Invoke-EnrichBatch {
    if ($forwardArgs.Count -gt 0) {
        docker compose exec -T baby-name-picker dotnet BabyNamePicker.dll enrich-names @forwardArgs
    } else {
        docker compose exec -T baby-name-picker dotnet BabyNamePicker.dll enrich-names
    }
    if ($LASTEXITCODE -ne 0) {
        throw "enrich-names exited with code $LASTEXITCODE"
    }
}

if ($all) {
    while ($true) {
        $output = Invoke-EnrichBatch | Out-String
        Write-Host $output.TrimEnd()
        if ($output -match 'Enrichment complete: 0/0 ') {
            Write-Host "No more names to enrich."
            break
        }
    }
} else {
    Invoke-EnrichBatch
}
