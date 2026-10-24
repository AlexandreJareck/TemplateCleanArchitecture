<#
.SYNOPSIS
    Phase 14 isolated Docker Compose baseline: clean-volume startup, PostgreSQL bootstrap, migrations/seeds twice,
    smoke tests, non-destructive restart, persistence, and shutdown.
.DESCRIPTION
    Uses a new, unique Compose project (p14-baseline-<RunId>) with docker-compose.yml plus docker-compose.baseline.yml,
    so containers, the network, and volumes are new and never the normal environment's. Credentials are random synthetic
    values written to an untracked env file under artifacts/validation/compose-baseline/<RunId> (git-ignored) and are
    never printed. The script never removes containers, volumes, images, or networks: it ends with `docker compose stop`.
    Evidence (checks, sanitized logs, captured OpenAPI) is written to the same folder.
    Phase 15: the default topology has one Redis 8 service (Search and Time Series integrated, no RedisGraph). Pass
    -RedisRollback to run the same baseline with docker-compose.redis-rollback.yml (the preserved pre-Phase 15 Redis
    8.4.0 + legacy module topology) layered on top; the Redis topology checks then assert the rollback shape instead.
    Phase 17: the broker is RabbitMQ 4.3.6 (management image, fixed node name, rabbitmq4-data). Pass -RabbitMqRollback
    to layer docker-compose.rabbitmq-rollback.yml (the Phase 14 4.2.2 artifact on rabbitmq-lib) instead.
    Phase 18: Redis Insight is 3.8.0 on redis-insight-data and nothing depends on it; the baseline proves WebApi and
    Redis keep working while it is stopped and that the stack starts without it. Pass -RedisInsightRollback to layer
    docker-compose.redis-insight-rollback.yml (the Phase 14 Redis Insight 1.14.0 artifact on redis-insight-volume_data).
    The baseline never accepts the Redis Insight EULA (scripts/redis-insight-upgrade-rehearsal.ps1 covers connections).
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$WebApiPort = 18080,
    [int]$StartupTimeoutSeconds = 900,
    [switch]$RedisRollback,
    # Phase 16: run the baseline with docker-compose.postgres-rollback.yml (the Phase 14 PostgreSQL 16.11 artifact).
    [switch]$PostgresRollback,
    # Phase 17: run the baseline with docker-compose.rabbitmq-rollback.yml (the Phase 14 RabbitMQ 4.2.2 artifact).
    [switch]$RabbitMqRollback,
    # Phase 18: run the baseline with docker-compose.redis-insight-rollback.yml (the Phase 14 Redis Insight 1.14.0 artifact).
    [switch]$RedisInsightRollback
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$project = if ($RedisInsightRollback) { "p18-rollback-$RunId" } elseif ($RabbitMqRollback) { "p17-rollback-$RunId" } elseif ($PostgresRollback) { "p16-rollback-$RunId" } elseif ($RedisRollback) { "p15-rollback-$RunId" } else { "p14-baseline-$RunId" }
$composeFiles = @('-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml')
if ($RedisRollback) { $composeFiles += @('-f', 'docker-compose.redis-rollback.yml') }
if ($PostgresRollback) { $composeFiles += @('-f', 'docker-compose.postgres-rollback.yml') }
if ($RabbitMqRollback) { $composeFiles += @('-f', 'docker-compose.rabbitmq-rollback.yml') }
if ($RedisInsightRollback) { $composeFiles += @('-f', 'docker-compose.redis-insight-rollback.yml') }
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\compose-baseline\$RunId"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$envFile = Join-Path $outputDirectory 'baseline.env'
$baseUri = "http://127.0.0.1:$WebApiPort"
$results = New-Object System.Collections.Generic.List[object]

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function Invoke-Compose {
    $arguments = @('compose', '-p', $project, '--env-file', $envFile) + $composeFiles + $args
    & docker @arguments
}

function Invoke-Sql([string]$DatabaseVariable, [string]$Sql) {
    # The SQL goes through stdin and the credentials stay inside the container's own environment. The database names
    # are passed as psql variables (:'app_db', :'identity_db'); no double quotes in the native argument (PowerShell 5.1).
    $arguments = @('compose', '-p', $project, '--env-file', $envFile) + $composeFiles + @(
        'exec', '-T', 'db', 'sh', '-c',
        "psql -U `$POSTGRES_USER -d `$$DatabaseVariable -tA -v ON_ERROR_STOP=1 -v app_db=`$POSTGRES_DB_NAME -v identity_db=`$POSTGRES_IDENTITY_DB_NAME")
    $output = $Sql | & docker @arguments 2>&1
    return ($output | Out-String).Trim()
}

function Get-DatabaseSnapshot {
    $app = Invoke-Sql 'POSTGRES_DB_NAME' @'
SELECT 'migrations=' || count(*) || ':' || coalesce(string_agg("MigrationId", ',' ORDER BY "MigrationId"), '') FROM "__EFMigrationsHistory";
SELECT 'products=' || count(*) || ':distinct=' || count(DISTINCT "Id") || ':' || coalesce(string_agg("Id"::text, ',' ORDER BY "Id"), '') FROM "Products";
SELECT 'identity_tables_in_app_db=' || count(*) FROM information_schema.tables WHERE table_schema = 'Identity';
'@
    $identity = Invoke-Sql 'POSTGRES_IDENTITY_DB_NAME' @'
SELECT 'migrations=' || count(*) || ':' || coalesce(string_agg("MigrationId", ',' ORDER BY "MigrationId"), '') FROM public."__EFMigrationsHistory";
SELECT 'roles=' || count(*) || ':distinct=' || count(DISTINCT "NormalizedName") || ':' || coalesce(string_agg("NormalizedName", ',' ORDER BY "NormalizedName"), '') FROM "Identity"."Role";
SELECT 'users=' || count(*) || ':distinct=' || count(DISTINCT "NormalizedUserName") || ':' || coalesce(string_agg("NormalizedUserName", ',' ORDER BY "NormalizedUserName"), '') FROM "Identity"."User";
SELECT 'user_roles=' || count(*) || ':distinct=' || count(DISTINCT ("UserId", "RoleId")) FROM "Identity"."UserRoles";
SELECT 'products_table_in_identity_db=' || count(*) FROM information_schema.tables WHERE table_name = 'Products';
'@
    return "[application]`n$app`n[identity]`n$identity"
}

function Wait-HttpOk([string]$Path, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$baseUri$Path" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Wait-SqlValue([string]$DatabaseVariable, [string]$Sql, [string]$Expected, [int]$TimeoutSeconds = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ((Invoke-Sql $DatabaseVariable $Sql) -eq $Expected) { return $true }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-Status([string]$Method, [string]$Path, [string]$Body, [hashtable]$Headers = @{}) {
    try {
        $parameters = @{ Uri = "$baseUri$Path"; Method = $Method; UseBasicParsing = $true; TimeoutSec = 30; Headers = $Headers }
        if ($Body) { $parameters.Body = $Body; $parameters.ContentType = 'application/json' }
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return [pscustomobject]@{ Status = $status; Content = '' }
    }
}

function Get-ServiceStates {
    $lines = Invoke-Compose ps -a --format '{{.Service}}|{{.State}}|{{.Health}}|{{.ExitCode}}' 2>$null
    return ($lines | Sort-Object) -join '; '
}

function Get-NormalVolumes {
    $names = docker volume ls --format '{{.Name}}' | Where-Object { $_ -like 'cleanarchitecture_*' -or $_ -like 'templatecleanarchnet_*' }
    return ($names | ForEach-Object { "$_@" + (docker volume inspect $_ --format '{{.CreatedAt}}') } | Sort-Object) -join ';'
}

# ---------------------------------------------------------------- 0. Isolation guard and synthetic environment
$existing = docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Names}}'
$existingVolumes = docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}'
if ($existing -or $existingVolumes) { throw "Compose project $project already has resources; choose another RunId." }
$normalVolumesBefore = Get-NormalVolumes

function New-Secret { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }
# Phase 22: a per-run JWT signing key from a cryptographically secure source (48 bytes, above JwtSettings.MinimumKeyBytes).
# It exists only in memory and in the git-ignored run env file, and is never printed or logged.
function New-JwtSigningKey { $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Convert]::ToBase64String($bytes) }
$redisPassword = New-Secret
$jwtSigningKey = New-JwtSigningKey
$envLines = Get-Content .env.example | ForEach-Object {
    if ($_ -match '^(\w+)=CHANGE_ME$') { "$($Matches[1])=$(if ($Matches[1] -eq 'REDIS_PASSWORD') { $redisPassword } elseif ($Matches[1] -eq 'JwtSettings__Key') { $jwtSigningKey } else { New-Secret })" }
    elseif ($_ -match '^APPDATA=') { 'APPDATA=/nonexistent' }
    elseif ($_ -match '^(\w+)_PORT=') { $null }
    else { $_ }
}
$envLines += "P14_WEBAPI_PORT=$WebApiPort"
$envLines | Set-Content -Path $envFile -Encoding ascii
Write-Check 'isolation.project-is-new' $true "project=$project"

# ---------------------------------------------------------------- 1. Static configuration checks
Invoke-Compose config -q 2>&1 | Out-Null
Write-Check 'compose.config-renders' ($LASTEXITCODE -eq 0)
$defaultConfig = docker compose --env-file .env.example config -q 2>&1
Write-Check 'compose.default-config-renders' ($LASTEXITCODE -eq 0)

$images = @(Invoke-Compose config --images 2>$null | Where-Object { $_ -and $_ -notlike "$project-*" })
$dockerfileImages = @(Select-String -Path 'Src\Presentation\Template.WebApi\Dockerfile','Src\Workers\Template.Worker\Dockerfile' -Pattern '^FROM\s+(\S+)' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Where-Object { $_ -notin @('base', 'build', 'publish') })
$allImages = $images + $dockerfileImages
$mutable = @($allImages | Where-Object { $_ -notmatch '^[^@\s]+:[0-9][^@\s:]*@sha256:[0-9a-f]{64}$' })
Write-Check 'images.all-pinned-version-and-digest' ($mutable.Count -eq 0) ("count=$($allImages.Count) unpinned=" + ($mutable -join ','))
$allImages | Set-Content (Join-Path $outputDirectory 'images.txt')

$expectedMajors = [ordered]@{
    'postgres' = '16.'; 'redis' = '8.'; 'rabbitmq' = '4.'; 'docker.elastic.co/elasticsearch/elasticsearch' = '9.';
    'docker.elastic.co/kibana/kibana' = '9.'; 'mcr.microsoft.com/dotnet/aspnet' = '10.0.';
    'mcr.microsoft.com/dotnet/sdk' = '10.0.'
}
if ($RedisInsightRollback) { $expectedMajors['redislabs/redisinsight'] = '1.' } else { $expectedMajors['redis/redisinsight'] = '3.' }
if ($RedisRollback) {
    $expectedMajors['redislabs/redisgraph'] = '2.10.'; $expectedMajors['redislabs/redisearch'] = '2.6.'; $expectedMajors['redislabs/redistimeseries'] = '1.6.'
}
foreach ($repository in $expectedMajors.Keys) {
    $match = @($allImages | Where-Object { ($_ -split '@')[0] -match ('^' + [regex]::Escape($repository) + ':') })
    $ok = $match.Count -gt 0 -and @($match | Where-Object { (($_ -split '@')[0] -split ':')[-1].StartsWith($expectedMajors[$repository]) }).Count -eq $match.Count
    Write-Check "images.major-unchanged.$repository" $ok ($match -join ',')
}

# Phase 15 Redis topology (static): one Redis 8 service on redis8-data, no module services, Insight -> redis only.
$services = @(Invoke-Compose config --services 2>$null)
$legacyRedisServices = @($services | Where-Object { $_ -in @('redis-graph', 'redis-search', 'redis-timeseries') })
$redisImage = @($images | Where-Object { $_ -like 'redis:*' })[0]
$configJson = Invoke-Compose config --format json 2>$null | Out-String | ConvertFrom-Json
$redisVolume = @($configJson.services.redis.volumes | ForEach-Object { $_.source })[0]
$insightDependencies = @($configJson.services.'redis-insight'.depends_on.PSObject.Properties.Name | Sort-Object) -join ','
if ($RedisRollback) {
    Write-Check 'redis.rollback-image-8.4.0' ($redisImage -like 'redis:8.4.0@sha256:47200b04*') $redisImage
    Write-Check 'redis.rollback-source-volume' ($redisVolume -eq 'redis-volume-data') $redisVolume
    Write-Check 'redis.rollback-legacy-services-restored' ($legacyRedisServices.Count -eq 3) ($legacyRedisServices -join ',')
}
else {
    Write-Check 'redis.single-redis8-target-image' ($redisImage -like 'redis:8.10.2@sha256:2c2dff79*') $redisImage
    Write-Check 'redis.target-volume-redis8-data' ($redisVolume -eq 'redis8-data') $redisVolume
    Write-Check 'redis.no-legacy-module-services' ($legacyRedisServices.Count -eq 0) ($services -join ',')
    Write-Check 'redis.insight-depends-on-redis-only' ($insightDependencies -eq 'redis') $insightDependencies
    $staleReferences = @(git grep -n -E 'redis-graph|redis-search|redis-timeseries|REDIS_(GRAPH|SEARCH|TIMESERIES)_PORT' -- docker-compose.yml docker-compose.override.yml docker-compose.baseline.yml .env.example scripts Tests Src 2>$null |
        Where-Object { $_ -notmatch '^scripts/(compose-baseline|redis8-migration-rehearsal|redis-insight-upgrade-rehearsal)\.ps1:' })
    Write-Check 'redis.no-removed-service-references' ($staleReferences.Count -eq 0) ($staleReferences -join ' | ')
}

$postgresImage = @($images | Where-Object { $_ -like 'postgres:*' })[0]
# Phase 16: 16.15 by default; the rollback overlay selects the Phase 14 artifact 16.11.
$expectedPostgres = if ($PostgresRollback) { 'postgres:16.11@sha256:056b54f0*' } else { 'postgres:16.15@sha256:ca0bd484*' }
Write-Check 'postgres.expected-image' ($postgresImage -like $expectedPostgres) $postgresImage
# Phase 17: 4.3.6 management image with a fixed node name on rabbitmq4-data; the rollback overlay restores the
# Phase 14 4.2.2 artifact on rabbitmq-lib with the original container-id hostname.
$rabbitImage = @($images | Where-Object { $_ -like 'rabbitmq:*' })[0]
$rabbitVolume = @($configJson.services.rabbitmq.volumes | Where-Object { $_.target -like '/var/lib/rabbitmq*' } | ForEach-Object { $_.source })[0]
$rabbitHostname = [string]$configJson.services.rabbitmq.hostname
if ($RabbitMqRollback) {
    Write-Check 'rabbitmq.rollback-image-4.2.2' ($rabbitImage -like 'rabbitmq:4.2.2@sha256:18cc2597*') $rabbitImage
    Write-Check 'rabbitmq.rollback-source-volume' ($rabbitVolume -eq 'rabbitmq-lib') $rabbitVolume
    Write-Check 'rabbitmq.rollback-original-hostname' ([string]::IsNullOrEmpty($rabbitHostname)) $rabbitHostname
}
else {
    Write-Check 'rabbitmq.target-image-4.3.6-management' ($rabbitImage -like 'rabbitmq:4.3.6-management@sha256:8dd6e357*') $rabbitImage
    Write-Check 'rabbitmq.target-volume-rabbitmq4-data' ($rabbitVolume -eq 'rabbitmq4-data') $rabbitVolume
    Write-Check 'rabbitmq.fixed-hostname' ($rabbitHostname -eq 'rabbitmq') $rabbitHostname
}

# Phase 18: Redis Insight is disposable tooling. Target 3.8.0 on the new redis-insight-data volume (never the 1.14
# volume); the rollback overlay restores 1.14.0 on redis-insight-volume_data. No service depends on it, and WebApi
# carries no Redis Insight setting.
$insightImage = @($images | Where-Object { $_ -like '*redisinsight:*' })[0]
$insightConfig = $configJson.services.'redis-insight'
$insightMounts = @($insightConfig.volumes | ForEach-Object { "$($_.source):$($_.target)" }) -join ','
$insightDependents = @($configJson.services.PSObject.Properties | Where-Object { $_.Value.depends_on -and $_.Value.depends_on.PSObject.Properties.Name -contains 'redis-insight' } | ForEach-Object { $_.Name })
$webApiEnvironment = ($configJson.services.webapi.environment | ConvertTo-Json -Compress)
$workerEnvironment = ($configJson.services.worker.environment | ConvertTo-Json -Compress)
if ($RedisInsightRollback) {
    Write-Check 'redis-insight.rollback-image-1.14.0' ($insightImage -like 'redislabs/redisinsight:1.14.0@sha256:b03ab142*') $insightImage
    Write-Check 'redis-insight.rollback-legacy-volume' ($insightMounts -eq 'redis-insight-volume_data:/db') $insightMounts
}
else {
    Write-Check 'redis-insight.target-image-3.8.0' ($insightImage -like 'redis/redisinsight:3.8.0@sha256:b5e19ee2*') $insightImage
    Write-Check 'redis-insight.target-volume-only' ($insightMounts -eq 'redis-insight-data:/data') $insightMounts
    $insightEnvironment = @($insightConfig.environment.PSObject.Properties.Name | Sort-Object) -join ','
    Write-Check 'redis-insight.preconfigured-connection-keys' ($insightEnvironment -eq 'RI_ENCRYPTION_KEY,RI_REDIS_ALIAS,RI_REDIS_HOST,RI_REDIS_PASSWORD,RI_REDIS_PORT' -and $insightConfig.environment.RI_REDIS_HOST -eq 'redis') $insightEnvironment
}
Write-Check 'redis-insight.no-dependents' ($insightDependents.Count -eq 0) ($insightDependents -join ',')
Write-Check 'redis-insight.webapi-has-no-insight-setting' ($webApiEnvironment -notmatch '(?i)insight|RI_') ''
Write-Check 'worker.no-public-port-or-fixed-container-name' (-not $configJson.services.worker.ports -and -not $configJson.services.worker.container_name) ''
$workerCredentialsResolved = $configJson.services.worker.environment.ConnectionStrings__DefaultConnection -match [regex]::Escape([string]$configJson.services.db.environment.POSTGRES_PASSWORD) `
    -and $configJson.services.worker.environment.RabbitMqSettings__Password -ceq $configJson.services.rabbitmq.environment.RABBITMQ_DEFAULT_PASS
Write-Check 'worker.credentials-from-environment' $workerCredentialsResolved ''
# Phase 22: the WebApi receives the signing key from the run env file through interpolation (value never shown).
Write-Check 'jwt.key-from-environment' ($configJson.services.webapi.environment.JwtSettings__Key -ceq $jwtSigningKey) ''
$committedJwtKeys = @(git grep -n -E 'JwtSettings__Key[:=] *[^$C ]' -- 'docker-compose*.yml' .env.example 2>$null)
Write-Check 'jwt.no-committed-key' ($committedJwtKeys.Count -eq 0) ''
# Committed files reference the secrets only as ${VARIABLE} interpolations.
$committedSecrets = @(git grep -n -E 'RI_(REDIS_PASSWORD|ENCRYPTION_KEY)[:=] *[^$ ]' -- 'docker-compose*.yml' .env.example 2>$null)
Write-Check 'redis-insight.no-committed-credentials' ($committedSecrets.Count -eq 0) ($committedSecrets -join ' | ')

$initScript = Join-Path $repositoryRoot 'init-db.sh'
docker run --rm -v "${initScript}:/tmp/init-db.sh:ro" --entrypoint bash $postgresImage -n /tmp/init-db.sh 2>&1 | Out-Null
Write-Check 'init-db.syntax' ($LASTEXITCODE -eq 0) 'bash -n inside the pinned PostgreSQL image'
$missing = docker run --rm -e POSTGRES_USER=synthetic -v "${initScript}:/tmp/init-db.sh:ro" --entrypoint bash $postgresImage /tmp/init-db.sh 2>&1 | Out-String
Write-Check 'init-db.missing-variable-fails' ($LASTEXITCODE -ne 0 -and $missing -match 'POSTGRES_DB_NAME must be set') ($missing.Trim())
$scriptText = Get-Content $initScript -Raw
Write-Check 'init-db.no-embedded-credentials' (-not ($scriptText -match '(?i)password|PGPASSWORD|Host=')) ''
Write-Check 'init-db.lf-line-endings' (-not $scriptText.Contains("`r`n")) ''

# ---------------------------------------------------------------- 2. Clean-volume startup (first run)
$started = Get-Date
Invoke-Compose up -d --build --wait --wait-timeout $StartupTimeoutSeconds 2>&1 | Set-Content (Join-Path $outputDirectory '01-up.log')
$upCode = $LASTEXITCODE
$elapsed = [int]((Get-Date) - $started).TotalSeconds
$webApiRunning = (Invoke-Compose ps --format '{{.Service}}|{{.State}}' 2>$null) -contains 'webapi|running'
$workerRunning = (Invoke-Compose ps --format '{{.Service}}|{{.State}}' 2>$null) -contains 'worker|running'
if (-not $webApiRunning -or -not $workerRunning) { $upCode = 1 }
Write-Check 'startup.compose-up-wait' ($upCode -eq 0) "seconds=$elapsed states=$(Get-ServiceStates)"
if ($upCode -ne 0) {
    # Later steps would only report noise; stop non-destructively and keep the resources for diagnosis.
    Invoke-Compose stop 2>&1 | Out-Null
    $results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
    "project=$project checks=$($results.Count) failed=startup" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
    exit 1
}
$volumes = (docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}' | Sort-Object) -join ','
Write-Check 'isolation.new-project-volumes' ($volumes -like "$project*") $volumes

$order = Invoke-Compose ps -a -q | ForEach-Object {
    # Container names are <project>-<service>-<n>; no quoted template arguments (PowerShell 5.1 strips them).
    $line = docker inspect $_ --format '{{.Name}}|{{.State.StartedAt}}'
    # Docker reports nanoseconds; .NET parses at most seven fractional digits.
    $line -replace "^/$([regex]::Escape($project))-(.+)-\d+\|", '$1|' -replace '(\.\d{7})\d+', '$1'
} | Sort-Object { [datetime](($_ -split '\|')[1]) }
$order | Set-Content (Join-Path $outputDirectory '02-start-order.txt')
$startedAt = @{}
$order | ForEach-Object { $parts = $_ -split '\|'; $startedAt[$parts[0]] = [datetime]$parts[1] }
$prerequisites = 'db', 'elasticsearch', 'rabbitmq'
$startedAfter = @($prerequisites | Where-Object { $startedAt[$_] -lt $startedAt['webapi'] }).Count -eq $prerequisites.Count
Write-Check 'startup.webapi-started-after-prerequisites' $startedAfter (($order | ForEach-Object { ($_ -split '\|')[0] }) -join ' > ')
$workerPrerequisites = 'db', 'rabbitmq'
$workerStartedAfter = @($workerPrerequisites | Where-Object { $startedAt[$_] -lt $startedAt['worker'] }).Count -eq $workerPrerequisites.Count
Write-Check 'startup.worker-started-after-prerequisites' $workerStartedAfter (($order | ForEach-Object { ($_ -split '\|')[0] }) -join ' > ')

Write-Check 'webapi.ready' (Wait-HttpOk '/health/ready' 180) '/health/ready'
$workerProbe = Invoke-Compose exec -T worker dotnet Template.Worker.dll --health-probe http://127.0.0.1:8080/health/ready 2>&1 | Out-String
Write-Check 'worker.ready' ($LASTEXITCODE -eq 0) ($workerProbe.Trim())
Invoke-Compose logs --no-color db 2>&1 | Select-String 'init-db.sh|database system is ready|Skipping initialization|CREATE DATABASE' |
    ForEach-Object { $_.Line } | Set-Content (Join-Path $outputDirectory '03-db-init.log')
$dbInitLog = Get-Content (Join-Path $outputDirectory '03-db-init.log') -Raw
Write-Check 'postgres.init-script-ran-on-clean-volume' ($dbInitLog -match 'running /docker-entrypoint-initdb.d/init-db.sh') ''

$databases = Invoke-Sql 'POSTGRES_USER' "SELECT count(*) FROM pg_database WHERE datname IN (:'app_db', :'identity_db') AND pg_get_userbyid(datdba) = current_user;"
Write-Check 'postgres.both-databases-created-and-owned' ($databases -eq '2') "count=$databases"
$postgresVersion = Invoke-Sql 'POSTGRES_USER' "SELECT current_setting('server_version');"
Write-Check 'postgres.runtime-version' ($postgresVersion -like $(if ($PostgresRollback) { '16.11*' } else { '16.15*' })) "version=$postgresVersion"
$createdAgain = Invoke-Compose exec -T db bash /docker-entrypoint-initdb.d/init-db.sh 2>&1 | Out-String
$databasesAgain = Invoke-Sql 'POSTGRES_USER' "SELECT count(*) FROM pg_database WHERE datname IN (:'app_db', :'identity_db');"
Write-Check 'init-db.rerun-idempotent' ($LASTEXITCODE -eq 0 -and $databasesAgain -eq '2') "count=$databasesAgain"

$migrationFiles = @{
    application = @(Get-ChildItem 'Src\Infrastructure\Template.Infrastructure.Persistence\Migrations' -Filter '*_*.cs' | Where-Object { $_.Name -notlike '*.Designer.cs' }).Count
    identity = @(Get-ChildItem 'Src\Infrastructure\Template.Infrastructure.Identity\Migrations' -Filter '*_*.cs' | Where-Object { $_.Name -notlike '*.Designer.cs' }).Count
}
# Docker keeps only the last five probe results, so the ordering proof is that the first startup never had to retry a
# database connection: migrations ran once, against databases that already accepted queries.
$firstStartLog = Invoke-Compose logs --no-color webapi 2>&1 | Out-String
Write-Check 'startup.webapi-no-database-connection-errors' ($firstStartLog -notmatch '(?i)Failed to connect|Connection refused|database .* does not exist|NpgsqlException') ''
$firstWorkerLog = Invoke-Compose logs --no-color worker 2>&1 | Out-String
Write-Check 'startup.worker-no-database-connection-errors' ($firstWorkerLog -notmatch '(?i)Failed to connect|Connection refused|database .* does not exist|NpgsqlException') ''
$snapshot1 = Get-DatabaseSnapshot
$snapshot1 | Set-Content (Join-Path $outputDirectory '04-db-snapshot-run1.txt')
$appMigrations = [regex]::Matches($snapshot1, 'migrations=(\d+)')
Write-Check 'migrations.application-history' ($appMigrations.Count -eq 2 -and [int]$appMigrations[0].Groups[1].Value -eq $migrationFiles.application) "expected=$($migrationFiles.application) actual=$($appMigrations[0].Groups[1].Value)"
Write-Check 'migrations.identity-history' ($appMigrations.Count -eq 2 -and [int]$appMigrations[1].Groups[1].Value -eq $migrationFiles.identity) "expected=$($migrationFiles.identity) actual=$($appMigrations[1].Groups[1].Value)"
Write-Check 'migrations.no-cross-wiring' ($snapshot1 -match 'identity_tables_in_app_db=0' -and $snapshot1 -match 'products_table_in_identity_db=0') ''
Write-Check 'seeds.products' ($snapshot1 -match 'products=5:distinct=5') ''
Write-Check 'seeds.roles-users' ($snapshot1 -match 'roles=(\d+):distinct=\1' -and $snapshot1 -match 'users=1:distinct=1') ''

# ---------------------------------------------------------------- 3. Smoke tests
foreach ($path in '/health', '/health/live', '/health/ready') {
    $response = Get-Status 'GET' $path
    Write-Check "smoke.$path" ($response.Status -eq 200) "status=$($response.Status)"
    $response.Content | Set-Content (Join-Path $outputDirectory ("health-" + ($path.Trim('/') -replace '/', '-') + '.json'))
}
# Contract baseline: Phase 27 is the last approved HTTP surface. Phase 28 changes messaging internals only.
foreach ($version in 'v1', 'v2') {
    $response = Get-Status 'GET' "/swagger/$version/swagger.json"
    $captured = Join-Path $outputDirectory "openapi-$version.json"
    [System.IO.File]::WriteAllText($captured, $response.Content)
    $baseline = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot "docs\refactoring\baseline-phase-32\openapi-$version.json"))
    $bytesEqual = ($baseline -replace "`r`n", "`n") -ceq ($response.Content -replace "`r`n", "`n")
    $structuralEqual = (($baseline | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress) -ceq (($response.Content | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress)
    Write-Check "openapi.$version.byte-equal-normalized" $bytesEqual ''
    Write-Check "openapi.$version.structural-equal" $structuralEqual ''
}

$productId = 'c9770569-499d-4b25-59af-08dd5e76cb62'
Write-Check 'smoke.product-v1-get-by-id' ((Get-Status 'GET' "/api/v1/product/get-by-id?id=$productId").Status -eq 200) ''
Write-Check 'smoke.product-v2-get' ((Get-Status 'GET' "/api/v2/product/$productId").Status -eq 200) ''
Write-Check 'smoke.product-v2-paged' ((Get-Status 'GET' '/api/v2/product/get-paged-list?PageNumber=1&PageSize=5').Status -eq 200) ''
Write-Check 'smoke.product-v1-unauthenticated-post-401' ((Get-Status 'POST' '/api/v1/product' '{}').Status -eq 401) ''

$cacheResponse = Get-Status 'GET' "/api/v2/product/$productId/cache"
$cacheKeys = Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning --scan --pattern 'template:product:v1:*' 2>&1 | Out-String
Write-Check 'integration.redis-cache-read-through' ($cacheResponse.Status -eq 200 -and $cacheKeys -match $productId) "status=$($cacheResponse.Status)"
$redisInfo = Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning INFO server 2>&1 | Out-String
$redisModules = Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning MODULE LIST 2>&1 | Out-String
$redisVersion = if ($redisInfo -match 'redis_version:(\S+)') { $Matches[1] } else { 'unknown' }
if ($RedisRollback) {
    Write-Check 'redis.runtime-version' ($redisVersion -eq '8.4.0') "version=$redisVersion"
}
else {
    Write-Check 'redis.runtime-version' ($redisVersion -eq '8.10.2') "version=$redisVersion"
    Write-Check 'redis.integrated-search-and-timeseries' ($redisModules -match '(?m)^search\s*$' -and $redisModules -match '(?m)^timeseries\s*$') ''
}
# WebApi is connected to the redis service of this project (its address appears in CLIENT LIST), and the Redis Insight
# container can reach and authenticate to it over the project network (connection management through the UI is covered
# by scripts/redis-insight-upgrade-rehearsal.ps1; the baseline does not accept the EULA).
$webApiAddress = (Invoke-Compose exec -T webapi sh -c 'hostname -i' 2>$null | Out-String).Trim()
$clients = Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning CLIENT LIST 2>&1 | Out-String
Write-Check 'redis.webapi-connected-to-target' ($webApiAddress -and $clients -match ('addr=' + [regex]::Escape(($webApiAddress -split '\s+')[0]) + ':')) ''
# Redis Insight 3.x runs on Node.js (no Python); 1.14 runs on Python. Both probes print the two RESP replies the same way.
if ($RedisInsightRollback) {
    $probeScript = "import os,socket,time`ns=socket.create_connection(('redis',6379),5)`ns.sendall(('AUTH '+os.environ['P15_PROBE_SECRET']+'\r\nPING\r\n').encode())`ntime.sleep(0.5)`nprint(s.recv(100).decode().split())`n"
    $probeRunner = 'python3'
}
else {
    $probeScript = "const s=require('net').connect(6379,'redis',()=>s.write('AUTH '+process.env.P15_PROBE_SECRET+'\r\nPING\r\n'));let d='';s.on('data',c=>{d+=c;const r=d.trim().split('\r\n');if(r.length>1){console.log('['+r.map(x=>String.fromCharCode(39)+x+String.fromCharCode(39)).join(', ')+']');s.end()}});setTimeout(()=>process.exit(1),5000).unref()"
    $probeRunner = 'node'
}
$probeEncoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($probeScript))
# The probe travels as base64 (Windows PowerShell 5.1 mangles quotes in native arguments).
$insightProbe = Invoke-Compose exec -T -e "P15_PROBE_SECRET=$redisPassword" -e "P15_PROBE=$probeEncoded" redis-insight sh -c "echo `$P15_PROBE | base64 -d > /tmp/p18-probe && $probeRunner /tmp/p18-probe; rm -f /tmp/p18-probe" 2>&1 | Out-String
Write-Check 'redis.insight-reaches-and-authenticates' ($insightProbe -match "'\+OK', '\+PONG'") ($insightProbe.Trim() -replace [regex]::Escape($redisPassword), '***')
if ($RedisInsightRollback) {
    $insightHealth = Invoke-Compose exec -T redis-insight python -c "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:5540/healthcheck/', timeout=5).read().decode())" 2>&1 | Out-String
    Write-Check 'redis-insight.rollback-legacy-healthcheck' ($insightHealth.Trim() -eq 'OK') ($insightHealth.Trim())
}
else {
    $insightHealth = Invoke-Compose exec -T redis-insight wget -q -O - http://127.0.0.1:5540/api/health/ 2>&1 | Out-String
    $insightInfo = Invoke-Compose exec -T redis-insight wget -q -O - http://127.0.0.1:5540/api/info 2>&1 | Out-String
    $insightSettings = Invoke-Compose exec -T redis-insight wget -q -O - http://127.0.0.1:5540/api/settings 2>&1 | Out-String
    Write-Check 'redis-insight.health-up' ($insightHealth -match '"status":"up"') ($insightHealth.Trim())
    Write-Check 'redis-insight.runtime-version-3.8.0' ($insightInfo -match '"appVersion":"3\.8\.0"' -and $insightInfo -match '"buildType":"DOCKER_ON_PREMISE"') ''
    Write-Check 'redis-insight.eula-not-auto-accepted' ($insightSettings -match '"agreements":null') ''
}

# The seed user's credentials are read from the committed seed source, never duplicated here.
$seedSource = Get-Content 'Src\Infrastructure\Template.Infrastructure.Identity\Seeds\DefaultBasicUser.cs' -Raw
$seedPassword = [regex]::Match($seedSource, 'CreateAsync\(defaultUser,\s*"([^"]+)"\)').Groups[1].Value
$seedUser = [regex]::Match($seedSource, 'UserName\s*=\s*"([^"]+)"').Groups[1].Value
$authBody = @{ userName = $seedUser; password = $seedPassword } | ConvertTo-Json -Compress
$auth = Get-Status 'POST' '/api/v2/account/authenticate' $authBody
$token = $null
if ($auth.Status -eq 200) { $token = ($auth.Content | ConvertFrom-Json).data.jwToken }
Write-Check 'integration.identity-authenticate-v2' ($auth.Status -eq 200 -and $token) "status=$($auth.Status)"
if ($token) {
    $badLogin = Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = 'synthetic-wrong-password' } | ConvertTo-Json -Compress)
    Write-Check 'integration.identity-wrong-password-rejected' ($badLogin.Status -ge 400 -and $badLogin.Status -lt 500) "status=$($badLogin.Status)"
    # Phase 22: the issued token authorizes a protected endpoint (404 for an unknown id, nothing is written);
    # the same call without a token is challenged.
    $probePath = "/api/v2/product/$([guid]::NewGuid())"
    $authorized = Get-Status 'DELETE' $probePath '' @{ Authorization = "Bearer $token" }
    Write-Check 'integration.jwt-authorized-call' ($authorized.Status -eq 404) "status=$($authorized.Status)"
    $anonymous = Get-Status 'DELETE' $probePath ''
    Write-Check 'integration.jwt-anonymous-call-401' ($anonymous.Status -eq 401) "status=$($anonymous.Status)"
}

$order = @{ id = [guid]::Empty; productName = 'Phase14Synthetic'; quantity = 1; price = 1.5 } | ConvertTo-Json -Compress
$publish = Get-Status 'POST' '/api/v1/message/create-order-direct?routingKey=order.urgent' $order
Start-Sleep -Seconds 3
$queues = Invoke-Compose exec -T rabbitmq rabbitmqctl -q list_queues name durable messages 2>&1 | Out-String
$queues | Set-Content (Join-Path $outputDirectory '05-rabbitmq-queues.txt')
Write-Check 'integration.rabbitmq-publish' ($publish.Status -eq 200 -and $queues -match 'dead_letter') "status=$($publish.Status)"
# Consumers ack after success, so every non-dead-letter queue drains to zero ready messages.
$pending = @($queues -split "`n" | Where-Object { $_ -match '^(\S+)\s+\S+\s+(\d+)' -and $Matches[1] -notmatch 'dead_letter' -and [int]$Matches[2] -gt 0 })
$deadLettered = @($queues -split "`n" | Where-Object { $_ -match '^(\S+dead_letter)\s+\S+\s+(\d+)' -and [int]$Matches[2] -gt 0 })
Write-Check 'integration.rabbitmq-consumed-and-acked' ($pending.Count -eq 0 -and $deadLettered.Count -eq 0) "pending=$($pending.Count) deadLettered=$($deadLettered.Count)"
$rabbitVersion = (Invoke-Compose exec -T rabbitmq rabbitmq-diagnostics -q server_version 2>&1 | Out-String).Trim()
Write-Check 'rabbitmq.runtime-version' ($rabbitVersion -eq $(if ($RabbitMqRollback) { '4.2.2' } else { '4.3.6' })) "version=$rabbitVersion"
$rabbitPlugins = ((Invoke-Compose exec -T rabbitmq rabbitmq-plugins -q list -e -m 2>&1 | Where-Object { $_ } | Sort-Object) -join ',')
Write-Check 'rabbitmq.plugins-explicit-management-only' ($rabbitPlugins -eq 'rabbitmq_management,rabbitmq_management_agent,rabbitmq_web_dispatch') $rabbitPlugins
$rabbitFlags = Invoke-Compose exec -T rabbitmq rabbitmqctl -q -s list_feature_flags name state stability 2>&1 | Out-String
$disabledFlags = @($rabbitFlags -split "`r?`n" | Where-Object { $_ -match '^(\S+)\t(\S+)\t(stable|required)$' -and $Matches[2] -ne 'enabled' })
Write-Check 'rabbitmq.stable-feature-flags-enabled' ($disabledFlags.Count -eq 0 -and $rabbitFlags -match '(?m)^khepri_db\tenabled') ($disabledFlags -join ',')
if (-not $RabbitMqRollback) {
    $rabbitStatus = Invoke-Compose exec -T rabbitmq rabbitmq-diagnostics -q status 2>&1 | Out-String
    Write-Check 'rabbitmq.fixed-node-name' ($rabbitStatus -match 'Node name: rabbit@rabbitmq\s') ''
}

# Phase 28: WebApi and Worker deploy and stop independently.
Invoke-Compose stop worker 2>&1 | Out-Null
$webApiWithoutWorker = Get-Status 'GET' "/api/v2/product/$productId"
Write-Check 'worker-absent.webapi-operates' ($webApiWithoutWorker.Status -eq 200) "status=$($webApiWithoutWorker.Status)"
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 worker 2>&1 | Out-Null
Write-Check 'worker-absent.worker-recovers' ($LASTEXITCODE -eq 0 -and (Get-ServiceStates) -match 'worker\|running\|healthy') ''

# Phase 28: an Outbox row accumulated while the broker is unavailable remains pending, then the Worker publishes it
# after recovery and the Inbox applies its protected effect once.
$backlogEventId = [guid]::NewGuid().ToString('D')
$backlogAggregateId = [guid]::NewGuid().ToString('D')
Invoke-Compose stop rabbitmq 2>&1 | Out-Null
$backlogSql = @'
INSERT INTO "MessagingOutbox"
    ("EventId", "EventType", "EventVersion", "OccurredAtUtc", "CorrelationId", "CausationId",
     "AggregateType", "AggregateId", "AggregateVersion", "PayloadJson", "CreatedAtUtc", "AttemptCount",
     "NextAttemptAtUtc", "ClaimOwner", "ClaimExpiresAtUtc", "DispatchedAtUtc", "PoisonedAtUtc", "LastFailureCode")
VALUES ('__EVENT__', 'procurement.requisition.expired', 1, now(), 'compose-phase28', NULL,
        'PurchaseRequisition', '__AGGREGATE__', '1', '{"requisitionId":"__AGGREGATE__","revision":1}'::jsonb,
        now(), 0, now(), NULL, NULL, NULL, NULL, NULL);
'@
$backlogSql = $backlogSql.Replace('__EVENT__', $backlogEventId).Replace('__AGGREGATE__', $backlogAggregateId)
Invoke-Sql 'POSTGRES_DB_NAME' $backlogSql | Out-Null
Start-Sleep -Seconds 3
$pendingDuringOutage = Invoke-Sql 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingOutbox"" WHERE ""EventId"" = '$backlogEventId' AND ""DispatchedAtUtc"" IS NULL;"
Write-Check 'worker.broker-outage-keeps-outbox-pending' ($pendingDuringOutage -eq '1') "pending=$pendingDuringOutage"
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 rabbitmq 2>&1 | Out-Null
$recovered = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingOutbox"" WHERE ""EventId"" = '$backlogEventId' AND ""DispatchedAtUtc"" IS NOT NULL;" '1'
$inboxApplied = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingInbox"" WHERE ""EventId"" = '$backlogEventId' AND ""CompletedAtUtc"" IS NOT NULL;" '1'
Write-Check 'worker.broker-recovery-drains-backlog' ($recovered -and $inboxApplied) "dispatched=$recovered inbox=$inboxApplied"

# A database outage must not kill the process: liveness stays up, readiness fails, and both recover after PostgreSQL.
Invoke-Compose stop db 2>&1 | Out-Null
$workerLiveWithoutDb = Invoke-Compose exec -T worker dotnet Template.Worker.dll --health-probe http://127.0.0.1:8080/health/live 2>&1 | Out-String
$workerLiveCode = $LASTEXITCODE
$workerReadyWithoutDb = Invoke-Compose exec -T worker dotnet Template.Worker.dll --health-probe http://127.0.0.1:8080/health/ready 2>&1 | Out-String
$workerReadyCode = $LASTEXITCODE
Write-Check 'worker.postgres-outage-is-observable' ($workerLiveCode -eq 0 -and $workerReadyCode -ne 0) "live=$workerLiveCode ready=$workerReadyCode"
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 db 2>&1 | Out-Null
$workerReadyAfterDbCode = 1
$workerReadyAfterDb = ''
for ($attempt = 0; $attempt -lt 30 -and $workerReadyAfterDbCode -ne 0; $attempt++) {
    $workerReadyAfterDb = Invoke-Compose exec -T worker dotnet Template.Worker.dll --health-probe http://127.0.0.1:8080/health/ready 2>&1 | Out-String
    $workerReadyAfterDbCode = $LASTEXITCODE
    if ($workerReadyAfterDbCode -ne 0) { Start-Sleep -Seconds 2 }
}
Write-Check 'worker.postgres-recovery-restores-readiness' ($workerReadyAfterDbCode -eq 0) ($workerReadyAfterDb.Trim())
Write-Check 'webapi.postgres-recovery-restores-readiness' (Wait-HttpOk '/health/ready' 120) ''

$createIndex = Get-Status 'POST' '/api/v1/person/create-index?indexName=persons'
$addPerson = Get-Status 'POST' '/api/v1/person/add-person' (@{ id = 9014; firstName = 'Synthetic' } | ConvertTo-Json -Compress)
$getPerson = Get-Status 'GET' '/api/v1/person/get-person/9014'
Write-Check 'integration.elasticsearch-person-index' ($createIndex.Status -eq 200 -and $addPerson.Status -eq 200 -and $getPerson.Status -eq 200) "create-index=$($createIndex.Status) add=$($addPerson.Status) get=$($getPerson.Status)"

$logStream = $false
for ($attempt = 0; $attempt -lt 20 -and -not $logStream; $attempt++) {
    $streams = Invoke-Compose exec -T elasticsearch curl -s 'http://localhost:9200/_data_stream/logs-*' 2>&1 | Out-String
    $logStream = $streams -match '"name"\s*:\s*"logs-'
    if (-not $logStream) { Start-Sleep -Seconds 3 }
}
Write-Check 'integration.elasticsearch-log-data-stream' $logStream ''
Write-Check 'service.kibana-healthy' ((Get-ServiceStates) -match 'kibana\|running\|healthy') ''
Write-Check 'service.redis-insight-healthy' ((Get-ServiceStates) -match 'redis-insight\|running\|healthy') ''

# Phase 18: Redis Insight stopped -> WebApi readiness, the Redis cache and Redis itself are unaffected.
Invoke-Compose stop redis-insight 2>&1 | Out-Null
Write-Check 'insight-absent.stopped' ((Get-ServiceStates) -match 'redis-insight\|exited') ''
$readyWithoutInsight = Get-Status 'GET' '/health/ready'
Write-Check 'insight-absent.webapi-ready' ($readyWithoutInsight.Status -eq 200 -and $readyWithoutInsight.Content -notmatch '(?i)insight') "status=$($readyWithoutInsight.Status)"
Write-Check 'insight-absent.cache-read-through' ((Get-Status 'GET' "/api/v2/product/$productId/cache").Status -eq 200) ''
$pingWithoutInsight = (Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning PING 2>&1 | Out-String).Trim()
Write-Check 'insight-absent.redis-ping' ($pingWithoutInsight -eq 'PONG') $pingWithoutInsight
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 redis-insight 2>&1 | Out-Null
Write-Check 'insight-absent.restarted-healthy' ((Get-ServiceStates) -match 'redis-insight\|running\|healthy') ''

# Phase 30: reports end to end on the isolated stack. The seed user receives the Administrator, BudgetController and
# Requester roles in the run's own identity database; the WebApi queues the job (202), the Worker renders it (ClosedXML
# for XLSX, the container's headless Chromium for PDF), stores it in the private Azurite container and the API streams it.
function Wait-ReportReady([string]$JobId, [hashtable]$Headers, [int]$TimeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $state = Get-Status 'GET' "/api/v2/report-jobs/$JobId" '' $Headers
        if ($state.Status -eq 200) {
            $view = $state.Content | ConvertFrom-Json
            if ($view.status -in @('Ready', 'Failed', 'Cancelled', 'Expired')) { return $view }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Save-ReportContent([string]$JobId, [hashtable]$Headers, [string]$Name) {
    $target = Join-Path $outputDirectory $Name
    try {
        $response = Invoke-WebRequest -Uri "$baseUri/api/v2/report-jobs/$JobId/content" -Headers $Headers -UseBasicParsing -TimeoutSec 60 -OutFile $target -PassThru
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Disposition = [string]$response.Headers['Content-Disposition']; CacheControl = [string]$response.Headers['Cache-Control']; Path = $target }
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return [pscustomobject]@{ Status = $status; Disposition = ''; CacheControl = ''; Path = $target }
    }
}

function Test-Signature([string]$Path, [byte[]]$Signature) {
    if (-not (Test-Path $Path)) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt $Signature.Length) { return $false }
    for ($index = 0; $index -lt $Signature.Length; $index++) { if ($bytes[$index] -ne $Signature[$index]) { return $false } }
    return $true
}

$reportHeaders = @{ Authorization = "Bearer $token" }
$grantSql = @'
INSERT INTO "Identity"."UserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "Identity"."User" u CROSS JOIN "Identity"."Role" r
WHERE u."NormalizedUserName" = 'ADMIN' AND r."NormalizedName" IN ('ADMINISTRATOR', 'BUDGETCONTROLLER', 'REQUESTER')
ON CONFLICT DO NOTHING;
SELECT count(*) FROM "Identity"."UserRoles" ur JOIN "Identity"."User" u ON u."Id" = ur."UserId" WHERE u."NormalizedUserName" = 'ADMIN';
'@
$granted = Invoke-Sql 'POSTGRES_IDENTITY_DB_NAME' $grantSql
Write-Check 'reports.synthetic-roles-granted' ([int](($granted -split "`n")[-1]) -ge 3) "roles=$granted"

$azuriteImage = [string]$configJson.services.azurite.image
Write-Check 'azurite.image-3.37.0-pinned' ($azuriteImage -like 'mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1*') $azuriteImage
Write-Check 'azurite.no-published-port' (-not $configJson.services.azurite.ports) ''
Write-Check 'azurite.account-from-environment' ($configJson.services.azurite.environment.AZURITE_ACCOUNTS -notmatch 'Eby8vdM02x' -and $configJson.services.azurite.environment.AZURITE_ACCOUNTS -match ':') ''
Write-Check 'service.azurite-healthy' ((Get-ServiceStates) -match 'azurite\|running\|healthy') ''

$fyStart = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd'); $fyEnd = (Get-Date).AddDays(330).ToString('yyyy-MM-dd')
$period = Get-Status 'POST' '/api/v2/fiscal-periods' (@{ code = 'P30-FY'; name = 'Synthetic report period'; startDate = $fyStart; endDate = $fyEnd } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$periodId = if ($period.Status -eq 201) { ($period.Content | ConvertFrom-Json).id } else { $null }
Write-Check 'reports.fiscal-period-created' ($null -ne $periodId) "status=$($period.Status)"

$xlsxRequest = Get-Status 'POST' '/api/v2/report-jobs' (@{ reportType = 'BudgetConsumptionXlsx'; fiscalPeriodId = $periodId } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$xlsxJob = if ($xlsxRequest.Status -eq 202) { ($xlsxRequest.Content | ConvertFrom-Json).id } else { $null }
Write-Check 'reports.xlsx-accepted-202' ($null -ne $xlsxJob) "status=$($xlsxRequest.Status)"
$xlsxView = if ($xlsxJob) { Wait-ReportReady $xlsxJob $reportHeaders } else { $null }
Write-Check 'reports.worker-renders-xlsx' ($xlsxView -and $xlsxView.status -eq 'Ready' -and $xlsxView.contentUrl -eq "/api/v2/report-jobs/$xlsxJob/content") "status=$($xlsxView.status) failure=$($xlsxView.failureCode)"
$xlsxFile = Save-ReportContent $xlsxJob $reportHeaders 'report-budget.xlsx'
Write-Check 'reports.xlsx-download-streamed' ($xlsxFile.Status -eq 200 -and (Test-Signature $xlsxFile.Path ([byte[]](0x50, 0x4B, 0x03, 0x04))) -and $xlsxFile.Disposition -match 'attachment' -and $xlsxFile.CacheControl -match 'no-store') "status=$($xlsxFile.Status)"
$storedRow = Invoke-Sql 'POSTGRES_DB_NAME' "SELECT ""Status"" || ':' || char_length(""Sha256"") FROM ""FilesStoredFiles"" WHERE ""OwnerId"" = '$xlsxJob';"
Write-Check 'reports.metadata-in-postgres-bytes-in-azurite' ($storedRow -eq 'Available:64') $storedRow

$department = Get-Status 'POST' '/api/v2/departments' (@{ code = 'P30-D'; name = 'Synthetic reports department' } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$departmentId = if ($department.Status -eq 201) { ($department.Content | ConvertFrom-Json).id } else { $null }
$costCenter = Get-Status 'POST' '/api/v2/cost-centers' (@{ code = 'P30-C'; name = 'Synthetic reports cost center'; departmentId = $departmentId } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$costCenterId = if ($costCenter.Status -eq 201) { ($costCenter.Content | ConvertFrom-Json).id } else { $null }
$draft = Get-Status 'POST' '/api/v2/purchase-requisitions' (@{ costCenterId = $costCenterId; currency = 'BRL'; purpose = 'Synthetic requisition for the report baseline'; neededBy = (Get-Date).AddDays(20).ToString('yyyy-MM-dd') } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$requisitionId = if ($draft.Status -eq 201) { ($draft.Content | ConvertFrom-Json).id } else { $null }
Write-Check 'reports.synthetic-requisition-created' ($null -ne $requisitionId) "department=$($department.Status) costCenter=$($costCenter.Status) draft=$($draft.Status)"

# Worker restart recovery: a job requested while the Worker is stopped stays Pending and completes after it starts.
Invoke-Compose stop worker 2>&1 | Out-Null
$pdfRequest = Get-Status 'POST' '/api/v2/report-jobs' (@{ reportType = 'RequisitionPdf'; requisitionId = $requisitionId } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$pdfJob = if ($pdfRequest.Status -eq 202) { ($pdfRequest.Content | ConvertFrom-Json).id } else { $null }
Start-Sleep -Seconds 4
$pendingWithoutWorker = (Get-Status 'GET' "/api/v2/report-jobs/$pdfJob" '' $reportHeaders).Content | ConvertFrom-Json
Write-Check 'reports.pending-while-worker-stopped' ($pendingWithoutWorker.status -eq 'Pending') "status=$($pendingWithoutWorker.status)"
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 worker 2>&1 | Out-Null
$pdfView = if ($pdfJob) { Wait-ReportReady $pdfJob $reportHeaders } else { $null }
Write-Check 'reports.worker-restart-renders-pdf-with-container-chromium' ($pdfView -and $pdfView.status -eq 'Ready') "status=$($pdfView.status) failure=$($pdfView.failureCode)"
$pdfFile = Save-ReportContent $pdfJob $reportHeaders 'report-requisition.pdf'
Write-Check 'reports.pdf-download-streamed' ($pdfFile.Status -eq 200 -and (Test-Signature $pdfFile.Path ([System.Text.Encoding]::ASCII.GetBytes('%PDF-')))) "status=$($pdfFile.Status)"

# Storage outage: downloads answer 503 and a new job retries; procurement and the API stay available; then it recovers.
Invoke-Compose stop azurite 2>&1 | Out-Null
$outageDownload = Save-ReportContent $xlsxJob $reportHeaders 'report-outage.bin'
Write-Check 'reports.storage-outage-download-503' ($outageDownload.Status -eq 503) "status=$($outageDownload.Status)"
$outageRequest = Get-Status 'POST' '/api/v2/report-jobs' (@{ reportType = 'BudgetConsumptionXlsx'; fiscalPeriodId = $periodId; currency = 'BRL' } | ConvertTo-Json -Compress) ($reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() })
$outageJob = if ($outageRequest.Status -eq 202) { ($outageRequest.Content | ConvertFrom-Json).id } else { $null }
$retried = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT ""FailureCode"" FROM ""ReportingReportJobs"" WHERE ""Id"" = '$outageJob';" 'report.storage-unavailable' 120
Write-Check 'reports.storage-outage-retries-with-safe-code' $retried ''
Write-Check 'reports.storage-outage-api-operates' ((Get-Status 'GET' "/api/v2/purchase-requisitions/$requisitionId" '' $reportHeaders).Status -eq 200) ''
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 azurite 2>&1 | Out-Null
$recoveredView = if ($outageJob) { Wait-ReportReady $outageJob $reportHeaders 240 } else { $null }
Write-Check 'reports.storage-recovery-completes-job' ($recoveredView -and $recoveredView.status -eq 'Ready' -and $recoveredView.attempts -ge 2) "status=$($recoveredView.status) attempts=$($recoveredView.attempts)"

# Phase 31: approval orchestration on the isolated stack. The award submitted through the API reaches the
# WorkflowFunctions host through the Outbox, RabbitMQ and the Worker; the orchestration creates the instance and assigns
# level 1 through the Application activities (the cost center has no manager, so the ProcurementManager group, APR-006).
# The host outage answers 503 without change, the orchestration resumes after a restart, and the administrator cancels.
function New-Cnpj {
    $digits = [string](Get-Random -Minimum 10000000 -Maximum 99999999) + '0001'
    $check = {
        param([string]$Value, [int[]]$Weights)
        $sum = 0; for ($i = 0; $i -lt $Weights.Length; $i++) { $sum += ([int][string]$Value[$i]) * $Weights[$i] }
        $rest = $sum % 11; if ($rest -lt 2) { 0 } else { 11 - $rest }
    }
    $first = & $check $digits @(5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
    $second = & $check ($digits + $first) @(6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
    return "$digits$first$second"
}

function Invoke-Api([string]$Method, [string]$Path, $Body, [string]$Version = '') {
    $headers = $reportHeaders + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() }
    if ($Version) { $headers['If-Match'] = '"' + $Version + '"' }
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Compress -Depth 6 } else { '' }
    $result = Get-Status $Method $Path $json $headers
    $view = if ($result.Content) { $result.Content | ConvertFrom-Json } else { $null }
    return [pscustomobject]@{ Status = $result.Status; View = $view }
}

$functionsConfig = $configJson.services.'workflow-functions'
Write-Check 'approvals.functions-no-public-port-or-fixed-name' (-not $functionsConfig.ports -and -not $functionsConfig.container_name) ''
Write-Check 'approvals.internal-key-from-environment' ($functionsConfig.environment.ApprovalWorkflowSettings__Key -ceq $configJson.services.webapi.environment.ApprovalWorkflowSettings__Key -and
    $functionsConfig.environment.ApprovalWorkflowSettings__Key -ceq $configJson.services.worker.environment.ApprovalWorkflowSettings__Key -and
    $functionsConfig.environment.ApprovalWorkflowSettings__Key.Length -ge 32) ''
$workflowStorageImage = [string]$configJson.services.'workflow-storage'.image
Write-Check 'approvals.task-hub-storage-separate-and-pinned' ($workflowStorageImage -like 'mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1*' -and
    -not $configJson.services.'workflow-storage'.ports) $workflowStorageImage
Write-Check 'service.workflow-functions-healthy' ((Get-ServiceStates) -match 'workflow-functions\|running\|healthy') ''
Write-Check 'service.workflow-storage-healthy' ((Get-ServiceStates) -match 'workflow-storage\|running\|healthy') ''
$functionsReady = Invoke-Compose exec -T workflow-functions curl -fsS http://127.0.0.1/api/health/ready 2>&1 | Out-String
Write-Check 'approvals.functions-ready-database-and-task-hub' ($functionsReady -match '"status":"Healthy"') ($functionsReady.Trim())
$unauthenticated = Invoke-Compose exec -T workflow-functions sh -c 'curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1/api/approval-workflows/approval-00000000000000000000000000000001' 2>&1 | Out-String
Write-Check 'approvals.internal-api-requires-the-key' ($unauthenticated.Trim() -eq '401') ($unauthenticated.Trim())

$approvalGrant = @'
INSERT INTO "Identity"."UserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "Identity"."User" u CROSS JOIN "Identity"."Role" r
WHERE u."NormalizedUserName" = 'ADMIN' AND r."NormalizedName" IN ('BUYER', 'PROCUREMENTMANAGER')
ON CONFLICT DO NOTHING;
SELECT count(*) FROM "Identity"."UserRoles" ur JOIN "Identity"."User" u ON u."Id" = ur."UserId" WHERE u."NormalizedUserName" = 'ADMIN';
'@
$approvalRoles = Invoke-Sql 'POSTGRES_IDENTITY_DB_NAME' $approvalGrant
Write-Check 'approvals.synthetic-roles-granted' ([int](($approvalRoles -split "`n")[-1]) -ge 5) "roles=$approvalRoles"

# The Phase 30 period was only needed for a report and stays Planned; reservations need an Open period (BUD-004).
$plannedPeriod = Invoke-Api 'GET' "/api/v2/fiscal-periods/$periodId" $null
$openedPeriod = Invoke-Api 'POST' "/api/v2/fiscal-periods/$periodId/open" $null $plannedPeriod.View.version
Write-Check 'approvals.fiscal-period-open' ($openedPeriod.Status -eq 200 -and $openedPeriod.View.status -eq 'Open') "status=$($openedPeriod.Status) code=$($openedPeriod.View.code)"
$allocation = Invoke-Api 'POST' '/api/v2/budget-allocations' @{ fiscalPeriodId = $periodId; costCenterId = $costCenterId; currency = 'BRL'; amount = '100000' }
$category = Invoke-Api 'POST' '/api/v2/categories' @{ code = 'P31-G'; name = 'Synthetic approval category' }
$unit = Invoke-Api 'POST' '/api/v2/units-of-measure' @{ code = 'P31-U'; name = 'Unit'; allowedDecimals = 0 }
$requisition = Invoke-Api 'GET' "/api/v2/purchase-requisitions/$requisitionId" $null
$line = Invoke-Api 'POST' "/api/v2/purchase-requisitions/$requisitionId/lines" @{ description = 'Synthetic approval line'; kind = 'Good'; categoryId = $category.View.id;
    unitOfMeasureId = $unit.View.id; quantity = '1'; estimatedUnitPrice = @{ amount = '3000.00'; currency = 'BRL' } } $requisition.View.version
$submitted = Invoke-Api 'POST' "/api/v2/purchase-requisitions/$requisitionId/submit" $null $line.View.version
Write-Check 'approvals.requisition-submitted' ($submitted.Status -eq 200 -and $submitted.View.status -eq 'Submitted') "allocation=$($allocation.Status) line=$($line.Status) submit=$($submitted.Status) code=$($submitted.View.code)"

$supplier = Invoke-Api 'POST' '/api/v2/suppliers' @{ legalName = 'Synthetic Approval Supplier Ltda'; tradeName = 'Synthetic'; country = 'BR'; taxId = (New-Cnpj) }
$contacts = Invoke-Api 'PUT' "/api/v2/suppliers/$($supplier.View.id)/contacts" @{ contacts = @(@{ name = 'Synthetic Contact'; email = 'contact@example.invalid'; role = 'Sales' }) } $supplier.View.version
$addresses = Invoke-Api 'PUT' "/api/v2/suppliers/$($supplier.View.id)/addresses" @{ addresses = @(@{ type = 'Headquarters'; postalCode = '01310-100'; street = 'Avenida Paulista';
    number = '1000'; district = 'Bela Vista'; city = 'Sao Paulo'; state = 'SP'; country = 'BR' }) } $contacts.View.version
$categories = Invoke-Api 'PUT' "/api/v2/suppliers/$($supplier.View.id)/categories" @{ categoryIds = @($category.View.id) } $addresses.View.version
$activated = Invoke-Api 'POST' "/api/v2/suppliers/$($supplier.View.id)/activate" $null $categories.View.version
Write-Check 'approvals.supplier-active' ($activated.Status -eq 200) "status=$($activated.Status)"

$rfq = Invoke-Api 'POST' '/api/v2/requests-for-quotation' @{ requisitionId = $requisitionId }
$invited = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/invitations" @{ supplierId = $supplier.View.id } $rfq.View.version
$published = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/publish" @{ deadline = (Get-Date).ToUniversalTime().AddDays(2).ToString('o') } $invited.View.version
$quoted = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/quotes" @{ supplierId = $supplier.View.id; currency = 'BRL';
    lines = @(@{ rfqLineNumber = 1; quantityOffered = '1'; unitPrice = '2900.00' }); freight = '0'; validUntil = (Get-Date).AddDays(60).ToString('yyyy-MM-dd');
    deliveryDays = 7; paymentTerms = '30 days'; source = 'Email'; receivedAt = (Get-Date).ToUniversalTime().AddMinutes(-1).ToString('o') } $published.View.version
$closed = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/close" @{ reason = 'Synthetic early close for the approval baseline' } $quoted.View.version
$awardStarted = Get-Date
$awarded = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/award" @{ quoteRevisionId = $closed.View.comparison.recommendedQuoteRevisionId } $closed.View.version
$awardId = if ($awarded.Status -eq 200) { $awarded.View.awards[0].id } else { $null }
Write-Check 'approvals.award-submitted' ($null -ne $awardId) "rfq=$($rfq.Status) $($rfq.View.code) publish=$($published.Status) $($published.View.code) quote=$($quoted.Status) $($quoted.View.code) close=$($closed.Status) award=$($awarded.Status) $($awarded.View.code)"

$assigned = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT i.""Status"" || ':' || s.""Status"" || ':' || coalesce(s.""AssigneeRole"", '') FROM ""ApprovalInstances"" i JOIN ""ApprovalSteps"" s ON s.""InstanceId"" = i.""Id"" AND s.""Level"" = i.""CurrentLevel"" WHERE i.""AwardId"" = '$awardId';" 'Pending:Pending:ProcurementManager' 120
$assignSeconds = [int]((Get-Date) - $awardStarted).TotalSeconds
Write-Check 'approvals.outbox-worker-functions-assign-level-1' $assigned "seconds=$assignSeconds"
$instances = Invoke-Api 'GET' "/api/v2/approval-instances?requisitionId=$requisitionId" $null
$instanceId = if ($instances.Status -eq 200 -and $instances.View.items.Count -eq 1) { $instances.View.items[0].id } else { $null }
Write-Check 'approvals.instance-visible-through-api' ($null -ne $instanceId) "status=$($instances.Status)"
$workflow = Invoke-Api 'GET' "/api/v2/approval-instances/$instanceId/workflow" $null
Write-Check 'approvals.orchestration-running' ($workflow.Status -eq 200 -and $workflow.View.runtimeStatus -eq 'Running') "status=$($workflow.Status) runtime=$($workflow.View.runtimeStatus)"

Invoke-Compose stop workflow-functions 2>&1 | Out-Null
$detail = Invoke-Api 'GET' "/api/v2/approval-instances/$instanceId" $null
$unavailable = Invoke-Api 'POST' "/api/v2/approval-instances/$instanceId/steps/1/decision" @{ decision = 'Approved' } $detail.View.version
$unchanged = Invoke-Api 'GET' "/api/v2/approval-instances/$instanceId" $null
Write-Check 'approvals.functions-outage-decision-503-without-change' ($unavailable.Status -eq 503 -and $unchanged.View.version -eq $detail.View.version) "status=$($unavailable.Status)"
Write-Check 'approvals.functions-outage-api-operates' ((Get-Status 'GET' "/api/v2/purchase-requisitions/$requisitionId" '' $reportHeaders).Status -eq 200) ''
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 workflow-functions 2>&1 | Out-Null
$resumed = Invoke-Api 'GET' "/api/v2/approval-instances/$instanceId/workflow" $null
Write-Check 'approvals.orchestration-survives-host-restart' ($resumed.Status -eq 200 -and $resumed.View.runtimeStatus -eq 'Running') "runtime=$($resumed.View.runtimeStatus)"

$cancelled = Invoke-Api 'POST' "/api/v2/approval-instances/$instanceId/cancel" @{ reason = 'Synthetic administrative cancel in the baseline' } $unchanged.View.version
Write-Check 'approvals.administrative-cancel' ($cancelled.Status -eq 200 -and $cancelled.View.status -eq 'Cancelled') "status=$($cancelled.Status)"
$completed = $false
$deadline = (Get-Date).AddSeconds(180)
do {
    $state = Invoke-Api 'GET' "/api/v2/approval-instances/$instanceId/workflow" $null
    if ($state.View.runtimeStatus -eq 'Completed') { $completed = $true; break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)
Write-Check 'approvals.orchestration-completes-after-cancel' $completed "runtime=$($state.View.runtimeStatus)"
$afterCancel = Invoke-Api 'GET' "/api/v2/purchase-requisitions/$requisitionId" $null
Write-Check 'approvals.requisition-back-in-sourcing' ($afterCancel.View.status -eq 'InSourcing') "status=$($afterCancel.View.status)"
$outboxApproval = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingOutbox"" WHERE ""AggregateId"" = '$instanceId' AND ""EventType"" IN ('procurement.approval.started', 'procurement.approval.step-assigned', 'procurement.approval.cancelled') AND ""DispatchedAtUtc"" IS NOT NULL;" '3' 60
Write-Check 'approvals.events-dispatched' $outboxApproval ''
$functionsLog = Invoke-Compose logs --no-color workflow-functions 2>&1 | Out-String
Write-Check 'approvals.no-key-in-functions-log' (-not $functionsLog.Contains([string]$functionsConfig.environment.ApprovalWorkflowSettings__Key)) ''

# Phase 32: requisition to receipt on the isolated stack. The buyer awards the RFQ again (it is Closed after the Phase
# 31 cancel); a second synthetic user holding ProcurementManager (copied from the seed user's credential row, so no
# password is introduced) approves the single level through the real Functions host; the completed approval reaches the
# Worker purchasing consumer through Outbox and RabbitMQ, which creates exactly one draft. The buyer issues it while the
# Worker is stopped (document Pending, number allocated once), the Worker renders the PDF with Chromium after its restart,
# records the document, queues and delivers the supplier notification locally, and the order is received in full.
function Invoke-ApiAs([hashtable]$Headers, [string]$Method, [string]$Path, $Body, [string]$Version = '') {
    $requestHeaders = $Headers + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() }
    if ($Version) { $requestHeaders['If-Match'] = '"' + $Version + '"' }
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Compress -Depth 6 } else { '' }
    $result = Get-Status $Method $Path $json $requestHeaders
    $view = if ($result.Content) { $result.Content | ConvertFrom-Json } else { $null }
    return [pscustomobject]@{ Status = $result.Status; View = $view }
}

$managerSql = @'
CREATE TEMP TABLE p32_user AS SELECT * FROM "Identity"."User" WHERE "NormalizedUserName" = 'ADMIN';
UPDATE p32_user SET "Id" = gen_random_uuid(), "UserName" = 'p32manager', "NormalizedUserName" = 'P32MANAGER', "Email" = 'p32manager@example.invalid',
    "NormalizedEmail" = 'P32MANAGER@EXAMPLE.INVALID', "SecurityStamp" = gen_random_uuid()::text, "ConcurrencyStamp" = gen_random_uuid()::text, "Name" = 'Synthetic Manager';
INSERT INTO "Identity"."User" SELECT * FROM p32_user ON CONFLICT DO NOTHING;
INSERT INTO "Identity"."UserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "Identity"."User" u CROSS JOIN "Identity"."Role" r
WHERE u."NormalizedUserName" = 'P32MANAGER' AND r."NormalizedName" IN ('PROCUREMENTMANAGER', 'RECEIVER')
ON CONFLICT DO NOTHING;
SELECT count(*) FROM "Identity"."UserRoles" ur JOIN "Identity"."User" u ON u."Id" = ur."UserId" WHERE u."NormalizedUserName" = 'P32MANAGER';
'@
$managerRoles = Invoke-Sql 'POSTGRES_IDENTITY_DB_NAME' $managerSql
Write-Check 'purchasing.synthetic-manager-created' ([int](($managerRoles -split "`n")[-1]) -eq 2) "roles=$managerRoles"
$managerAuth = Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = 'p32manager'; password = $seedPassword } | ConvertTo-Json -Compress)
$managerToken = if ($managerAuth.Status -eq 200) { ($managerAuth.Content | ConvertFrom-Json).data.jwToken } else { $null }
$managerHeaders = @{ Authorization = "Bearer $managerToken" }
Write-Check 'purchasing.manager-authenticated' ($null -ne $managerToken) "status=$($managerAuth.Status)"

$closedRfq = Invoke-Api 'GET' "/api/v2/requests-for-quotation/$($rfq.View.id)" $null
$reawarded = Invoke-Api 'POST' "/api/v2/requests-for-quotation/$($rfq.View.id)/award" @{ quoteRevisionId = $closedRfq.View.comparison.recommendedQuoteRevisionId } $closedRfq.View.version
$poAwardId = if ($reawarded.Status -eq 200) { @($reawarded.View.awards | Where-Object { $_.status -eq 'PendingApproval' })[0].id } else { $null }
Write-Check 'purchasing.rfq-awarded-again' ($null -ne $poAwardId) "status=$($reawarded.Status) code=$($reawarded.View.code)"
$poAssigned = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT i.""Status"" || ':' || s.""Status"" FROM ""ApprovalInstances"" i JOIN ""ApprovalSteps"" s ON s.""InstanceId"" = i.""Id"" AND s.""Level"" = i.""CurrentLevel"" WHERE i.""AwardId"" = '$poAwardId';" 'Pending:Pending' 120
Write-Check 'purchasing.approval-assigned' $poAssigned ''
$poInstanceId = (Invoke-Sql 'POSTGRES_DB_NAME' "SELECT ""Id"" FROM ""ApprovalInstances"" WHERE ""AwardId"" = '$poAwardId';").Trim()
$poInstance = Invoke-ApiAs $managerHeaders 'GET' "/api/v2/approval-instances/$poInstanceId" $null
$approved = Invoke-ApiAs $managerHeaders 'POST' "/api/v2/approval-instances/$poInstanceId/steps/1/decision" @{ decision = 'Approved' } $poInstance.View.version
Write-Check 'purchasing.manager-approves-final-level' ($approved.Status -eq 200) "status=$($approved.Status) code=$($approved.View.code)"
$draftCreated = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(*) || ':' || min(""Status"") FROM ""PurchasingOrders"" WHERE ""AwardId"" = '$poAwardId';" '1:Draft' 180
Write-Check 'purchasing.functions-finalize-outbox-worker-create-one-draft' $draftCreated ''
$completedInbox = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingInbox"" i JOIN ""MessagingOutbox"" o ON o.""EventId"" = i.""EventId"" WHERE i.""ConsumerName"" = 'procurement-purchasing-v1' AND o.""AggregateId"" = '$poInstanceId' AND o.""EventType"" = 'procurement.approval.completed' AND i.""CompletedAtUtc"" IS NOT NULL;" '1' 60
Write-Check 'purchasing.approval-completed-consumed-once' $completedInbox ''

$orders = Invoke-Api 'GET' "/api/v2/purchase-orders?requisitionId=$requisitionId" $null
$orderId = if ($orders.Status -eq 200 -and $orders.View.items.Count -eq 1) { $orders.View.items[0].id } else { $null }
$draft = Invoke-Api 'GET' "/api/v2/purchase-orders/$orderId" $null
Write-Check 'purchasing.draft-snapshot' ($draft.Status -eq 200 -and $draft.View.total.amount -eq '2900.00' -and $draft.View.supplier.legalName -eq 'Synthetic Approval Supplier Ltda') "status=$($draft.Status) total=$($draft.View.total.amount)"
$updated = Invoke-Api 'PUT' "/api/v2/purchase-orders/$orderId" @{ deliveryAddress = 'Rua Sintetica 100, Sao Paulo - SP' } $draft.View.version
Write-Check 'purchasing.draft-delivery-address' ($updated.Status -eq 200) "status=$($updated.Status)"

Invoke-Compose stop worker 2>&1 | Out-Null
$issued = Invoke-Api 'POST' "/api/v2/purchase-orders/$orderId/issue" $null $updated.View.version
$poNumber = [string]$issued.View.number
Write-Check 'purchasing.issued-with-gapless-number' ($issued.Status -eq 200 -and $poNumber -match '^PO-\d{4}-000001$' -and $issued.View.document.status -eq 'Pending') "status=$($issued.Status) number=$poNumber"
$repeated = Invoke-Api 'POST' "/api/v2/purchase-orders/$orderId/issue" $null $updated.View.version
Write-Check 'purchasing.repeated-issue-same-number' ($repeated.Status -eq 200 -and $repeated.View.number -eq $poNumber) "status=$($repeated.Status)"
$requisitionOrdered = Invoke-Api 'GET' "/api/v2/purchase-requisitions/$requisitionId" $null
Write-Check 'purchasing.requisition-ordered' ($requisitionOrdered.View.status -eq 'Ordered' -and $requisitionOrdered.View.purchaseOrderId -eq $orderId) "status=$($requisitionOrdered.View.status)"
Write-Check 'purchasing.worker-outage-document-pending' ((Invoke-Api 'GET' "/api/v2/purchase-orders/$orderId" $null).View.document.status -eq 'Pending') ''
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout 180 worker 2>&1 | Out-Null
$documentReady = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT ""DocumentStatus"" || ':' || ""NotificationStatus"" FROM ""PurchasingOrders"" WHERE ""Id"" = '$orderId';" 'Ready:Sent' 240
Write-Check 'purchasing.worker-renders-document-and-notifies-after-restart' $documentReady ''
$deliveries = (Invoke-Sql 'POSTGRES_DB_NAME' "SELECT count(*) FROM ""MessagingNotificationDeliveries"" WHERE ""AggregateId"" = '$orderId' AND ""RecipientType"" = 'Supplier';").Trim()
Write-Check 'purchasing.supplier-notified-once-locally' ($deliveries -eq '1') "deliveries=$deliveries"
$documentPath = Join-Path $outputDirectory 'purchase-order.pdf'
function Save-OrderDocument([string]$Target) {
    try {
        $response = Invoke-WebRequest -Uri "$baseUri/api/v2/purchase-orders/$orderId/document" -Headers $reportHeaders -UseBasicParsing -TimeoutSec 60 -OutFile $Target -PassThru
        return [int]$response.StatusCode
    }
    catch { return $(if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }) }
}
$documentStatus = Save-OrderDocument $documentPath
Write-Check 'purchasing.document-downloaded-pdf' ($documentStatus -eq 200 -and (Test-Signature $documentPath ([byte[]](0x25, 0x50, 0x44, 0x46)))) "status=$documentStatus"
$documentFile = (Invoke-Sql 'POSTGRES_DB_NAME' "SELECT f.""Status"" || ':' || coalesce(f.""ExpiresAtUtc""::text, 'never') FROM ""PurchasingOrders"" o JOIN ""FilesStoredFiles"" f ON f.""Id"" = o.""DocumentFileId"" WHERE o.""Id"" = '$orderId';").Trim()
Write-Check 'purchasing.document-kept-without-expiry' ($documentFile -eq 'Available:never') $documentFile

$issuedOrder = Invoke-ApiAs $managerHeaders 'GET' "/api/v2/purchase-orders/$orderId" $null
$overReceipt = Invoke-ApiAs $managerHeaders 'POST' '/api/v2/receipts' @{ purchaseOrderId = $orderId; receivedOn = (Get-Date).ToString('yyyy-MM-dd');
    lines = @(@{ poLineNumber = 1; received = '2'; accepted = '2'; rejected = '0' }) }
Write-Check 'purchasing.over-receipt-rejected' ($overReceipt.Status -eq 422) "status=$($overReceipt.Status)"
$receiptPosted = Invoke-ApiAs $managerHeaders 'POST' '/api/v2/receipts' @{ purchaseOrderId = $orderId; receivedOn = (Get-Date).ToString('yyyy-MM-dd'); location = 'Dock 1';
    lines = @(@{ poLineNumber = 1; received = '1'; accepted = '1'; rejected = '0' }) }
Write-Check 'purchasing.receipt-completes-order' ($receiptPosted.Status -eq 201 -and $receiptPosted.View.purchaseOrder.status -eq 'Completed') "status=$($receiptPosted.Status) issued=$($issuedOrder.Status)"
$requisitionClosed = Invoke-Api 'GET' "/api/v2/purchase-requisitions/$requisitionId" $null
Write-Check 'purchasing.requisition-closed' ($requisitionClosed.View.status -eq 'Closed') "status=$($requisitionClosed.View.status)"
$purchasingEvents = Wait-SqlValue 'POSTGRES_DB_NAME' "SELECT count(DISTINCT ""EventType"") FROM ""MessagingOutbox"" WHERE ""AggregateId"" IN ('$orderId', '$($receiptPosted.View.receipt.id)') AND ""EventType"" IN ('procurement.purchase-order.drafted', 'procurement.purchase-order.issued', 'procurement.purchase-order.notification-requested', 'procurement.purchase-order.completed', 'procurement.receipt.posted') AND ""DispatchedAtUtc"" IS NOT NULL;" '5' 90
Write-Check 'purchasing.events-dispatched' $purchasingEvents ''
$numberCounter = (Invoke-Sql 'POSTGRES_DB_NAME' "SELECT ""LastValue"" FROM ""PurchasingOrderNumberCounters"" WHERE ""Year"" = $([int]$poNumber.Substring(3, 4));").Trim()
Write-Check 'purchasing.counter-advanced-once' ($numberCounter -eq '1') "lastValue=$numberCounter"

# Phase 30 changed the run's data on purpose (synthetic roles, master data and reports); the restart idempotency and
# persistence checks below compare against this state.
$snapshot1 = Get-DatabaseSnapshot
$snapshot1 | Set-Content (Join-Path $outputDirectory '04b-db-snapshot-after-reports.txt')

# ---------------------------------------------------------------- 4. Second application startup (idempotency)
$secondStart = (Get-Date).ToUniversalTime().ToString('o')
Invoke-Compose restart webapi 2>&1 | Out-Null
Write-Check 'restart.webapi-ready-again' (Wait-HttpOk '/health/ready' 180) ''
$snapshot2 = Get-DatabaseSnapshot
$snapshot2 | Set-Content (Join-Path $outputDirectory '06-db-snapshot-run2.txt')
Write-Check 'idempotency.second-startup-same-state' ($snapshot1 -ceq $snapshot2) ''
$firstRunMigrations = ((Invoke-Compose logs --no-color --until $secondStart webapi worker 2>&1) | Select-String 'Applying migration').Count
$secondRunMigrations = ((Invoke-Compose logs --no-color --since $secondStart webapi worker 2>&1) | Select-String 'Applying migration').Count
Write-Check 'idempotency.no-migration-applied-on-second-startup' ($firstRunMigrations -gt 0 -and $secondRunMigrations -eq 0) "first=$firstRunMigrations second=$secondRunMigrations"

# ---------------------------------------------------------------- 5. Non-destructive full stop/start
$stackRestart = (Get-Date).ToUniversalTime().ToString('o')
Invoke-Compose stop 2>&1 | Set-Content (Join-Path $outputDirectory '07-stop.log')
Write-Check 'restart.stack-stopped' ((Get-ServiceStates) -notmatch '\|running\|') (Get-ServiceStates)
# Phase 18: the stack starts without Redis Insight (every other service first), then Redis Insight joins.
$servicesWithoutInsight = @($services | Where-Object { $_ -ne 'redis-insight' })
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout $StartupTimeoutSeconds @servicesWithoutInsight 2>&1 | Set-Content (Join-Path $outputDirectory '08-restart.log')
$stackWithoutInsight = $LASTEXITCODE -eq 0
Write-Check 'insight-absent.stack-starts-without-insight' ($stackWithoutInsight -and (Get-ServiceStates) -match 'redis-insight\|exited') (Get-ServiceStates)
Write-Check 'insight-absent.webapi-ready-after-restart' (Wait-HttpOk '/health/ready' 180) ''
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout $StartupTimeoutSeconds 2>&1 | Add-Content (Join-Path $outputDirectory '08-restart.log')
Write-Check 'restart.stack-healthy-again' ($LASTEXITCODE -eq 0) (Get-ServiceStates)
Write-Check 'restart.webapi-ready' (Wait-HttpOk '/health/ready' 180) ''
$dbRestartLog = Invoke-Compose logs --no-color --since $stackRestart db 2>&1 | Out-String
Write-Check 'restart.init-script-not-rerun' ($dbRestartLog -match 'Skipping initialization' -and $dbRestartLog -notmatch 'init-db.sh') ''
$snapshot3 = Get-DatabaseSnapshot
$snapshot3 | Set-Content (Join-Path $outputDirectory '09-db-snapshot-run3.txt')
Write-Check 'restart.postgres-data-persisted' ($snapshot1 -ceq $snapshot3) ''
Write-Check 'restart.elasticsearch-person-persisted' ((Get-Status 'GET' '/api/v1/person/get-person/9014').Status -eq 200) ''
$queuesAfter = Invoke-Compose exec -T rabbitmq rabbitmqctl -q list_queues name durable 2>&1 | Out-String
Write-Check 'restart.rabbitmq-durable-queues-persisted' ($queuesAfter -match 'dead_letter') ''
Write-Check 'restart.product-v2-get' ((Get-Status 'GET' "/api/v2/product/$productId").Status -eq 200) ''
$persistedReport = Save-ReportContent $xlsxJob $reportHeaders 'report-after-restart.xlsx'
Write-Check 'restart.report-output-persisted-in-azurite' ($persistedReport.Status -eq 200 -and (Test-Signature $persistedReport.Path ([byte[]](0x50, 0x4B, 0x03, 0x04)))) "status=$($persistedReport.Status)"
$persistedOrderDocument = Join-Path $outputDirectory 'purchase-order-after-restart.pdf'
$persistedOrderStatus = Save-OrderDocument $persistedOrderDocument
Write-Check 'restart.purchase-order-document-persisted' ($persistedOrderStatus -eq 200 -and (Test-Signature $persistedOrderDocument ([byte[]](0x25, 0x50, 0x44, 0x46)))) "status=$persistedOrderStatus"

# ---------------------------------------------------------------- 6. Graceful shutdown without deleting volumes
Invoke-Compose stop 2>&1 | Set-Content (Join-Path $outputDirectory '10-final-stop.log')
$states = Get-ServiceStates
Write-Check 'shutdown.all-stopped' ($states -notmatch '\|running\|') $states
$webApiLog = Invoke-Compose logs --no-color --since $stackRestart webapi 2>&1 | Out-String
Write-Check 'shutdown.webapi-graceful' ($webApiLog -match 'Application stopping') ''
$workerLog = Invoke-Compose logs --no-color --since $stackRestart worker 2>&1 | Out-String
Write-Check 'shutdown.worker-graceful' ($workerLog -match 'Application (is shutting down|stopping)') ''
$volumesAfter = (docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}' | Sort-Object) -join ','
Write-Check 'shutdown.project-volumes-retained' ($volumesAfter -eq $volumes) $volumesAfter
Write-Check 'isolation.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''

# Sanitized bounded logs for review (the env file stays local and git-ignored).
Invoke-Compose logs --no-color --tail 400 webapi 2>&1 | ForEach-Object { $_ -replace '(?i)(password|pwd|token|authorization)[^\s,;]*', '$1=***' } |
    Set-Content (Join-Path $outputDirectory '11-webapi-tail.log')
Invoke-Compose logs --no-color --tail 400 worker 2>&1 | ForEach-Object { $_ -replace '(?i)(password|pwd|token|authorization)[^\s,;]*', '$1=***' } |
    Set-Content (Join-Path $outputDirectory '12-worker-tail.log')

# Phase 22: the signing key appears in no evidence file (the run env file is the only place that holds it).
$jwtLeaks = @(Get-ChildItem $outputDirectory -Recurse -File | Where-Object { $_.FullName -ne $envFile -and ([string](Get-Content $_.FullName -Raw)).Contains($jwtSigningKey) })
Write-Check 'secrets.jwt-key-not-in-evidence' ($jwtLeaks.Count -eq 0) ($jwtLeaks.Name -join ',')

$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$failed = @($results | Where-Object { -not $_.Passed })
"project=$project checks=$($results.Count) failed=$($failed.Count)" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
