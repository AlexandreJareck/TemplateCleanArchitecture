<#
.SYNOPSIS
    Runs the SC-01 browser journey (Client/Template.WebApp/e2e-real) against the running local Compose stack.

.DESCRIPTION
    Reads DEMO_USER_PASSWORD from the git-ignored .env and hands it to Playwright as E2E_DEMO_PASSWORD for this
    process only; the value is never printed or written anywhere. The stack must already be up with the demo seed
    enabled (docker compose up -d). The journey creates and completes its own requisition, so it changes local demo
    data by design. Requires the pinned Node/pnpm on PATH and the Playwright Chromium browser.

.PARAMETER BaseUrl
    Application URL (default http://localhost:4200, the Compose webapp).

.PARAMETER EnvironmentPath
    Path of the .env file (default: .env at the repository root).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\real-journey.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:4200',
    [string]$EnvironmentPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $EnvironmentPath) { $EnvironmentPath = Join-Path $repositoryRoot '.env' }
$webApp = Join-Path $repositoryRoot 'Client\Template.WebApp'

if (-not (Test-Path -LiteralPath $EnvironmentPath))
{
    throw ".env not found at $EnvironmentPath. Create it with scripts\setup-demo-env.ps1."
}

$password = $null
foreach ($line in Get-Content -LiteralPath $EnvironmentPath)
{
    if ($line -match '^\s*DEMO_USER_PASSWORD\s*=\s*(.*)$') { $password = $Matches[1].Trim() }
}
if ([string]::IsNullOrEmpty($password) -or $password -eq 'CHANGE_ME')
{
    throw 'DEMO_USER_PASSWORD is missing or still CHANGE_ME in .env.'
}

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue))
{
    throw 'pnpm was not found on PATH. Put the pinned Node 24.22.0 / pnpm 12.14.0 on PATH first (docs/frontend/toolchain-and-maintenance.md).'
}

try
{
    $response = Invoke-WebRequest -Uri $BaseUrl -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200) { throw "HTTP $($response.StatusCode)" }
}
catch
{
    throw "The application is not reachable at $BaseUrl. Start the stack with 'docker compose up -d'. ($($_.Exception.Message))"
}

$previousPassword = $env:E2E_DEMO_PASSWORD
$previousBaseUrl = $env:E2E_BASE_URL
Push-Location $webApp
try
{
    $env:E2E_DEMO_PASSWORD = $password
    $env:E2E_BASE_URL = $BaseUrl
    Write-Host "Running SC-01 against $BaseUrl (demo password taken from .env, not shown)."
    & pnpm exec playwright test -c playwright.real.config.ts
    $exitCode = $LASTEXITCODE
}
finally
{
    $env:E2E_DEMO_PASSWORD = $previousPassword
    $env:E2E_BASE_URL = $previousBaseUrl
    Pop-Location
}

if ($exitCode -ne 0) { Write-Host "SC-01 failed (exit $exitCode). Report: Client\Template.WebApp\playwright-report-real" }
exit $exitCode
