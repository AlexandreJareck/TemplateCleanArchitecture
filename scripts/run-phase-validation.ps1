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
    [string]$ElasticRehearsalWebApi9xPath
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
if ($ElasticRehearsalWebApi8xPath -and $ElasticRehearsalWebApi9xPath) {
    Invoke-Step '06d-elastic-upgrade-rehearsal' { & (Join-Path $PSScriptRoot 'elastic-upgrade-rehearsal.ps1') -WebApi8xPath $ElasticRehearsalWebApi8xPath -WebApi9xPath $ElasticRehearsalWebApi9xPath -DotNetPath $DotNetPath }
}
Invoke-Step '07-test-functional' { & $DotNetPath test Tests\Template.Functional.Tests\Template.Functional.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1 }
Invoke-Step '08-vulnerable-packages' { & $DotNetPath list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive }
Invoke-Step '09-docker-compose-config' { docker compose config -q }
Invoke-Step '10-capture-openapi' { & (Join-Path $PSScriptRoot 'capture-api-baseline.ps1') -DotNetPath $DotNetPath -OutputDirectory (Join-Path $logDirectory 'openapi') }
if (-not $SkipGraphify) {
    Invoke-Step '11-graphify' { graphify update . --force }
}
Invoke-Step '12-git-diff-check' { git diff --check; git diff --cached --check }

"Validation finished $(Get-Date -Format o)" | Add-Content $summary
Get-Content $summary
