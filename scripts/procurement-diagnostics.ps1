<#
.SYNOPSIS
    Phase 33 read-only diagnostics of the local Compose stack.

.DESCRIPTION
    Prints the Outbox backlog and poison count, Inbox progress per consumer, report jobs and failures, approval
    instances and notification deliveries (scripts/diagnostics/procurement-health.sql, read-only transaction), then
    the depth of every procurement queue and dead-letter queue in RabbitMQ and the readiness of the WebApi, Worker and
    WorkflowFunctions. Credentials are read from the git-ignored .env and passed only to the containers; nothing
    secret is printed. It never writes, purges or deletes anything.
#>
[CmdletBinding()]
param(
    [string]$EnvFile,
    [string]$PostgresContainer = 'templatecleanarch-postgres',
    [string]$RabbitMqContainer = 'templatecleanarch-rabbitmq'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults.
if (-not $EnvFile) { $EnvFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.env' }

function Read-EnvValue([string]$Name) {
    $line = Get-Content -LiteralPath $EnvFile | Where-Object { $_ -match "^$Name=" } | Select-Object -First 1
    if (-not $line) { throw "$Name is missing in the env file." }
    return $line.Substring($Name.Length + 1)
}

if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Env file not found: $EnvFile" }
$user = Read-EnvValue 'POSTGRES_USER'
$database = Read-EnvValue 'POSTGRES_DB_NAME'
$sql = Join-Path $PSScriptRoot 'diagnostics\procurement-health.sql'

Write-Host '== Application database (read-only)'
Get-Content -LiteralPath $sql -Raw | docker exec -i $PostgresContainer psql -U $user -d $database -v ON_ERROR_STOP=1 -q -f -
if ($LASTEXITCODE -ne 0) { throw 'The diagnostic queries failed.' }

Write-Host '== RabbitMQ procurement queues (messages ready / unacknowledged)'
docker exec $RabbitMqContainer rabbitmqctl -q list_queues name messages_ready messages_unacknowledged |
    Where-Object { $_ -match '^procurement\.' } | Sort-Object
if ($LASTEXITCODE -ne 0) { throw 'Listing RabbitMQ queues failed.' }

Write-Host '== Readiness'
$probes = [ordered]@{
    'webapi'            = 'http://localhost:8080/health/ready'
    'worker'            = 'http://localhost:8082/health/ready'
    'workflow-functions' = 'http://localhost:7071/api/health/ready'
}
foreach ($probe in $probes.GetEnumerator()) {
    try {
        $response = Invoke-WebRequest -Uri $probe.Value -UseBasicParsing -TimeoutSec 5
        Write-Host ('{0,-20} {1} {2}' -f $probe.Key, $response.StatusCode, $response.Content)
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'unreachable' }
        Write-Host ('{0,-20} {1}' -f $probe.Key, $status)
    }
}
