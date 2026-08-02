#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
    Write-Host "Created .env from .env.example"
}

function Get-DotEnvValue {
    param([string]$Key)

    if ((Get-Item -Path "env:$Key" -ErrorAction SilentlyContinue)?.Value) {
        return (Get-Item -Path "env:$Key").Value
    }

    if (-not (Test-Path .env)) {
        return $null
    }

    $line = Get-Content .env |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Key))\s*=" } |
        Select-Object -Last 1

    if (-not $line) {
        return $null
    }

    return ($line -split '=', 2)[1].Trim().Trim('"').Trim("'")
}

$network = Get-DotEnvValue -Key DOCKER_SHARED_NETWORK
if (-not $network) {
    throw "DOCKER_SHARED_NETWORK is not set. Add it to .env (see .env.example)."
}

$exists = docker network ls --format '{{.Name}}' | Where-Object { $_ -eq $network }
if ($exists) {
    Write-Host "Docker network '$network' already exists"
} else {
    docker network create $network
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create Docker network '$network'"
    }
    Write-Host "Created Docker network '$network'"
}

Write-Host "Setup complete. Run: docker compose up --build"
