# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
<#
.SYNOPSIS
    Local Docker deployment for Aetheus.
.PARAMETER Rebuild
    Force rebuild without cache.
.PARAMETER Down
    Stop containers (volumes and DB data are preserved).
.PARAMETER Wipe
    DESTRUCTIVE: stop containers AND delete named volumes (local DB data).
    Asks for interactive confirmation.
#>
param(
    [switch]$Rebuild,
    [switch]$Down,
    [switch]$Wipe
)

$ErrorActionPreference = "Stop"

$appName   = "aetheus"
$portFront = 10001
$portBack  = 10002
$dbUser    = "dbaetheus"
$dbPass    = "localdev123"
$jwtKey    = "local-dev-jwt-key-minimum-32-characters-long-for-hmac"
$compose   = Join-Path $PSScriptRoot "..\compose\local.compose.yml"
$context   = Join-Path $PSScriptRoot "..\..\.."

$env:APPNAME     = $appName
$env:PORT_FRONT  = $portFront
$env:PORT_BACK   = $portBack
$env:DB_USER     = $dbUser
$env:DB_PASSWORD = $dbPass
$env:JWT_KEY     = $jwtKey
$env:APP_VERSION = "local"

if ($Wipe) {
    Write-Host "This DELETES the '${appName}-local' named volumes (local DB data)." -ForegroundColor Red
    $answer = Read-Host "Type 'wipe' to confirm"
    if ($answer -ne 'wipe') {
        Write-Host "Aborted - nothing was removed." -ForegroundColor Yellow
        exit 1
    }
    docker compose -f $compose -p "${appName}-local" down -v
    exit 0
}

if ($Down) {
    Write-Host "Stopping containers (volumes preserved)..." -ForegroundColor Yellow
    docker compose -f $compose -p "${appName}-local" down
    exit 0
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Aetheus - Local Docker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Front: http://localhost:$portFront"
Write-Host "  Back:  http://localhost:$portBack"
Write-Host ""

$env:APPNAME     = $appName
$env:PORT_FRONT  = $portFront
$env:PORT_BACK   = $portBack
$env:DB_USER     = $dbUser
$env:DB_PASSWORD = $dbPass
$env:JWT_KEY     = $jwtKey
$env:APP_VERSION = "local"

if ($Rebuild) {
    docker compose -f $compose -p "${appName}-local" build --no-cache
}
docker compose -f $compose -p "${appName}-local" up -d --build

Write-Host ""
Write-Host "Containers started." -ForegroundColor Green
Write-Host "  Front: http://localhost:$portFront"
Write-Host "  Back:  http://localhost:$portBack"
