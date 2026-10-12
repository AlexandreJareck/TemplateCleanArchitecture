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
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$WebApiPort = 18080,
    [int]$StartupTimeoutSeconds = 900,
    [switch]$RedisRollback,
    # Phase 16: run the baseline with docker-compose.postgres-rollback.yml (the Phase 14 PostgreSQL 16.11 artifact).
    [switch]$PostgresRollback
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$project = if ($PostgresRollback) { "p16-rollback-$RunId" } elseif ($RedisRollback) { "p15-rollback-$RunId" } else { "p14-baseline-$RunId" }
$composeFiles = @('-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml')
if ($RedisRollback) { $composeFiles += @('-f', 'docker-compose.redis-rollback.yml') }
if ($PostgresRollback) { $composeFiles += @('-f', 'docker-compose.postgres-rollback.yml') }
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
$redisPassword = New-Secret
$envLines = Get-Content .env.example | ForEach-Object {
    if ($_ -match '^(\w+)=CHANGE_ME$') { "$($Matches[1])=$(if ($Matches[1] -eq 'REDIS_PASSWORD') { $redisPassword } else { New-Secret })" }
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
$dockerfileImages = @(Select-String -Path 'Src\Presentation\Template.WebApi\Dockerfile' -Pattern '^FROM\s+(\S+)' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Where-Object { $_ -notin @('base', 'build', 'publish') })
$allImages = $images + $dockerfileImages
$mutable = @($allImages | Where-Object { $_ -notmatch '^[^@\s]+:[0-9][^@\s:]*@sha256:[0-9a-f]{64}$' })
Write-Check 'images.all-pinned-version-and-digest' ($mutable.Count -eq 0) ("count=$($allImages.Count) unpinned=" + ($mutable -join ','))
$allImages | Set-Content (Join-Path $outputDirectory 'images.txt')

$expectedMajors = [ordered]@{
    'postgres' = '16.'; 'redis' = '8.'; 'rabbitmq' = '4.'; 'docker.elastic.co/elasticsearch/elasticsearch' = '9.';
    'docker.elastic.co/kibana/kibana' = '9.'; 'redislabs/redisinsight' = '1.'; 'mcr.microsoft.com/dotnet/aspnet' = '10.0.';
    'mcr.microsoft.com/dotnet/sdk' = '10.0.'
}
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
        Where-Object { $_ -notmatch '^scripts/(compose-baseline|redis8-migration-rehearsal)\.ps1:' })
    Write-Check 'redis.no-removed-service-references' ($staleReferences.Count -eq 0) ($staleReferences -join ' | ')
}

$postgresImage = @($images | Where-Object { $_ -like 'postgres:*' })[0]
# Phase 16: 16.15 by default; the rollback overlay selects the Phase 14 artifact 16.11.
$expectedPostgres = if ($PostgresRollback) { 'postgres:16.11@sha256:056b54f0*' } else { 'postgres:16.15@sha256:ca0bd484*' }
Write-Check 'postgres.expected-image' ($postgresImage -like $expectedPostgres) $postgresImage
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
if (-not $webApiRunning) { $upCode = 1 }
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

Write-Check 'webapi.ready' (Wait-HttpOk '/health/ready' 180) '/health/ready'
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
foreach ($version in 'v1', 'v2') {
    $response = Get-Status 'GET' "/swagger/$version/swagger.json"
    $captured = Join-Path $outputDirectory "openapi-$version.json"
    [System.IO.File]::WriteAllText($captured, $response.Content)
    $baseline = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot "docs\refactoring\baseline-phase-6\openapi-$version.json"))
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
# WebApi is connected to the redis service of this project (its address appears in CLIENT LIST), and Redis Insight can
# reach and authenticate to it (Insight 1.x has no connection provisioning by environment; its saved connections live in
# its own volume and are configured in the UI).
$webApiAddress = (Invoke-Compose exec -T webapi sh -c 'hostname -i' 2>$null | Out-String).Trim()
$clients = Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning CLIENT LIST 2>&1 | Out-String
Write-Check 'redis.webapi-connected-to-target' ($webApiAddress -and $clients -match ('addr=' + [regex]::Escape(($webApiAddress -split '\s+')[0]) + ':')) ''
$probeScript = "import os,socket,time`ns=socket.create_connection(('redis',6379),5)`ns.sendall(('AUTH '+os.environ['P15_PROBE_SECRET']+'\r\nPING\r\n').encode())`ntime.sleep(0.5)`nprint(s.recv(100).decode().split())`n"
$probeEncoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($probeScript))
# The probe travels as base64 (Windows PowerShell 5.1 mangles quotes in native arguments).
$insightProbe = Invoke-Compose exec -T -e "P15_PROBE_SECRET=$redisPassword" -e "P15_PROBE=$probeEncoded" redis-insight sh -c 'echo $P15_PROBE | base64 -d | python3' 2>&1 | Out-String
Write-Check 'redis.insight-reaches-and-authenticates' ($insightProbe -match "'\+OK', '\+PONG'") ($insightProbe.Trim() -replace [regex]::Escape($redisPassword), '***')

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
Write-Check 'service.redis-insight-running' ((Get-ServiceStates) -match 'redis-insight\|running') ''

# ---------------------------------------------------------------- 4. Second application startup (idempotency)
$secondStart = (Get-Date).ToUniversalTime().ToString('o')
Invoke-Compose restart webapi 2>&1 | Out-Null
Write-Check 'restart.webapi-ready-again' (Wait-HttpOk '/health/ready' 180) ''
$snapshot2 = Get-DatabaseSnapshot
$snapshot2 | Set-Content (Join-Path $outputDirectory '06-db-snapshot-run2.txt')
Write-Check 'idempotency.second-startup-same-state' ($snapshot1 -ceq $snapshot2) ''
$firstRunMigrations = (Invoke-Compose logs --no-color --until $secondStart webapi 2>&1 | Select-String 'Applying migration').Count
$secondRunMigrations = (Invoke-Compose logs --no-color --since $secondStart webapi 2>&1 | Select-String 'Applying migration').Count
Write-Check 'idempotency.no-migration-applied-on-second-startup' ($firstRunMigrations -gt 0 -and $secondRunMigrations -eq 0) "first=$firstRunMigrations second=$secondRunMigrations"

# ---------------------------------------------------------------- 5. Non-destructive full stop/start
$stackRestart = (Get-Date).ToUniversalTime().ToString('o')
Invoke-Compose stop 2>&1 | Set-Content (Join-Path $outputDirectory '07-stop.log')
Write-Check 'restart.stack-stopped' ((Get-ServiceStates) -notmatch '\|running\|') (Get-ServiceStates)
Invoke-Compose up -d --no-build --no-recreate --wait --wait-timeout $StartupTimeoutSeconds 2>&1 | Set-Content (Join-Path $outputDirectory '08-restart.log')
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

# ---------------------------------------------------------------- 6. Graceful shutdown without deleting volumes
Invoke-Compose stop 2>&1 | Set-Content (Join-Path $outputDirectory '10-final-stop.log')
$states = Get-ServiceStates
Write-Check 'shutdown.all-stopped' ($states -notmatch '\|running\|') $states
$webApiLog = Invoke-Compose logs --no-color --since $stackRestart webapi 2>&1 | Out-String
Write-Check 'shutdown.webapi-graceful' ($webApiLog -match 'Application stopping') ''
$volumesAfter = (docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}' | Sort-Object) -join ','
Write-Check 'shutdown.project-volumes-retained' ($volumesAfter -eq $volumes) $volumesAfter
Write-Check 'isolation.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''

# Sanitized bounded logs for review (the env file stays local and git-ignored).
Invoke-Compose logs --no-color --tail 400 webapi 2>&1 | ForEach-Object { $_ -replace '(?i)(password|pwd|token|authorization)[^\s,;]*', '$1=***' } |
    Set-Content (Join-Path $outputDirectory '11-webapi-tail.log')

$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$failed = @($results | Where-Object { -not $_.Passed })
"project=$project checks=$($results.Count) failed=$($failed.Count)" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
