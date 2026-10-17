<#
.SYNOPSIS
    Runs the sequential validation gates used for the refactoring checkpoints.
.DESCRIPTION
    Each step runs one after another (never in parallel) and its output is written to
    artifacts/validation/<step>.log (git-ignored). A summary with exit codes is written to
    artifacts/validation/summary.txt. The API contract is captured into
    artifacts/validation/openapi without overwriting docs/refactoring/baseline.
#>
param(
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [switch]$SkipGraphify,
    # Optional Phase 11 parallel upgrade rehearsal: published pre-upgrade (8.x) and upgraded (9.x) WebApi folders.
    [string]$ElasticRehearsalWebApi8xPath,
    [string]$ElasticRehearsalWebApi9xPath,
    # Optional Phase 14 isolated Compose baseline (new project and volumes; requires Docker).
    [switch]$ComposeBaseline,
    # Optional Phase 15 isolated Redis migration rehearsal and rollback-topology baseline (new projects and volumes).
    [switch]$RedisRehearsal,
    # Optional Phase 16 PostgreSQL 16 backup/restore/patch/rollback rehearsal and 16.11 rollback-image baseline.
    [switch]$PostgresRehearsal,
    # Optional Phase 17 RabbitMQ 4.2.2 -> 4.2.9 -> 4.3.6 rehearsal, broker suite against the 4.2.2 rollback source and
    # the 4.2.2 rollback-overlay baseline.
    [switch]$RabbitMqRehearsal,
    # Optional Phase 18 Redis Insight 1.14.0 -> 3.8.0 rehearsal (new tooling volume, Redis 8 connection, restart, absence,
    # rollback) and the Redis Insight 1.14.0 rollback-overlay baseline.
    [switch]$RedisInsightRehearsal
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$logDirectory = Join-Path $repositoryRoot 'artifacts\validation'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$summary = Join-Path $logDirectory 'summary.txt'
"Validation started $(Get-Date -Format o)" | Set-Content $summary

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    $log = Join-Path $logDirectory "$Name.log"
    Write-Host "==> $Name"
    $global:LASTEXITCODE = 0
    try {
        & $Command *>&1 | Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    }
    catch {
        $_ | Out-String | Add-Content $log
        $code = 'exception'
    }
    "$Name exit=$code" | Add-Content $summary
}

Invoke-Step '00-git-status' { git status --short }
Invoke-Step '01-restore' { & $DotNetPath restore TemplateCleanArchitecture.App.slnf --verbosity minimal --disable-parallel }
Invoke-Step '02-build-webapi' { & $DotNetPath build Src\Presentation\Template.WebApi\Template.WebApi.csproj --no-restore --verbosity minimal --maxcpucount:1 /p:UseSharedCompilation=false }
Invoke-Step '03-test-architecture' { & $DotNetPath test Tests\Template.Architecture.Tests\Template.Architecture.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '04-test-unit' { & $DotNetPath test Tests\Template.Unit.Tests\Template.Unit.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '05-test-integration' { & $DotNetPath test Tests\Template.Integration.Tests\Template.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '06-test-postgresql' { & $DotNetPath test Tests\Template.PostgreSql.Integration.Tests\Template.PostgreSql.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '06b-test-rabbitmq' { & $DotNetPath test Tests\Template.RabbitMq.Integration.Tests\Template.RabbitMq.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '06c-test-elasticsearch' { & $DotNetPath test Tests\Template.Elasticsearch.Integration.Tests\Template.Elasticsearch.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '06f-test-redis' { & $DotNetPath test Tests\Template.Redis.Integration.Tests\Template.Redis.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
if ($ElasticRehearsalWebApi8xPath -and $ElasticRehearsalWebApi9xPath) {
    Invoke-Step '06d-elastic-upgrade-rehearsal' { & (Join-Path $PSScriptRoot 'elastic-upgrade-rehearsal.ps1') -WebApi8xPath $ElasticRehearsalWebApi8xPath -WebApi9xPath $ElasticRehearsalWebApi9xPath -DotNetPath $DotNetPath }
}
if ($ComposeBaseline) {
    Invoke-Step '06e-compose-baseline' { & (Join-Path $PSScriptRoot 'compose-baseline.ps1') }
}
if ($RedisRehearsal) {
    Invoke-Step '06g-redis8-migration-rehearsal' { & (Join-Path $PSScriptRoot 'redis8-migration-rehearsal.ps1') }
    Invoke-Step '06h-compose-redis-rollback' { & (Join-Path $PSScriptRoot 'compose-baseline.ps1') -RedisRollback }
}
if ($PostgresRehearsal) {
    Invoke-Step '06i-postgres16-patch-rehearsal' { & (Join-Path $PSScriptRoot 'postgres16-patch-rehearsal.ps1') }
    Invoke-Step '06j-compose-postgres-rollback' { & (Join-Path $PSScriptRoot 'compose-baseline.ps1') -PostgresRollback }
}
if ($RabbitMqRehearsal) {
    Invoke-Step '06k-rabbitmq4-upgrade-rehearsal' { & (Join-Path $PSScriptRoot 'rabbitmq4-upgrade-rehearsal.ps1') }
    Invoke-Step '06l-test-rabbitmq-rollback-source' {
        $env:TEMPLATE_RABBITMQ_TEST_IMAGE = 'rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870'
        try { & $DotNetPath test Tests\Template.RabbitMq.Integration.Tests\Template.RabbitMq.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
        finally { Remove-Item Env:TEMPLATE_RABBITMQ_TEST_IMAGE }
    }
    Invoke-Step '06m-compose-rabbitmq-rollback' { & (Join-Path $PSScriptRoot 'compose-baseline.ps1') -RabbitMqRollback }
}
if ($RedisInsightRehearsal) {
    Invoke-Step '06n-redis-insight-upgrade-rehearsal' { & (Join-Path $PSScriptRoot 'redis-insight-upgrade-rehearsal.ps1') }
    Invoke-Step '06o-compose-redis-insight-rollback' { & (Join-Path $PSScriptRoot 'compose-baseline.ps1') -RedisInsightRollback }
}
Invoke-Step '07-test-functional' { & $DotNetPath test Tests\Template.Functional.Tests\Template.Functional.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
# Phase 19: coverage from a clean output folder (coverlet.collector, Cobertura) for the projects that reference the
# collector, then validation, per-module merge and enforcement of Tests/test-coverage-policy.json.
Invoke-Step '07b-coverage' {
    & (Join-Path $PSScriptRoot 'test-tooling-evidence.ps1') -Label 'coverage-gate' -DotNetPath $DotNetPath -Coverage -Projects @(
        'Tests\Template.Unit.Tests\Template.Unit.Tests.csproj',
        'Tests\Template.Integration.Tests\Template.Integration.Tests.csproj',
        'Tests\Template.Functional.Tests\Template.Functional.Tests.csproj')
    $collected = $LASTEXITCODE
    & (Join-Path $PSScriptRoot 'coverage-report.ps1') -InputDirectory (Join-Path $logDirectory 'test-tooling\coverage-gate')
    if ($collected -ne 0) { $global:LASTEXITCODE = $collected }
}
Invoke-Step '08-vulnerable-packages' { & $DotNetPath list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive }
# Phase 22: rendered with the placeholder template, never the user's .env (which may hold real values).
Invoke-Step '09-docker-compose-config' { docker compose --env-file .env.example config -q }
Invoke-Step '10-capture-openapi' { & (Join-Path $PSScriptRoot 'capture-api-baseline.ps1') -DotNetPath $DotNetPath -OutputDirectory (Join-Path $logDirectory 'openapi') }
if (-not $SkipGraphify) {
    Invoke-Step '11-graphify' { graphify update . --force }
}
Invoke-Step '12-git-diff-check' { git diff --check; git diff --cached --check }
# Phase 22: no secret value in committed configuration and the compromised JWT key nowhere, run evidence included.
Invoke-Step '13-secret-scan' { & (Join-Path $PSScriptRoot 'secret-scan.ps1') -EvidencePath (Join-Path $repositoryRoot 'artifacts\validation') }

"Validation finished $(Get-Date -Format o)" | Add-Content $summary
Get-Content $summary
