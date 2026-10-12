<#
.SYNOPSIS
    Phase 18 isolated Redis Insight 1.14.0 -> 3.8.0 rehearsal: new target volume, Redis 8 connection, safe data
    inspection, restart persistence, absence and rollback to the untouched legacy tooling.
.DESCRIPTION
    Uses a new Compose project p18-<RunId> with docker-compose.yml + docker-compose.baseline.yml and a generated overlay
    (artifacts/validation/redis-insight-rehearsal/<RunId>, git-ignored) that publishes the target on a loopback port and
    adds a redis-insight-legacy service built from the values of docker-compose.redis-insight-rollback.yml (image,
    RIPORT, /db volume), so the target and the legacy tooling never replace each other's container. Only the redis,
    redis-insight and redis-insight-legacy services start; WebApi checks with Redis Insight absent live in
    scripts/compose-baseline.ps1.
    Sequence: Redis 8 with synthetic cache/Search/Time Series data -> legacy 1.14 with a synthetic saved connection ->
    legacy stopped -> 3.8.0 on the new redis-insight-data volume -> EULA accepted only in this isolated project
    (analytics and notifications off) -> preconfigured connection from RI_REDIS_* -> bounded read-only inspection ->
    wrong-password test -> restart -> target stopped with Redis unaffected -> legacy restarted on its untouched volume.
    Credentials are random synthetic values in an untracked env file and are never printed. Nothing is removed: the
    script ends with `docker compose stop`.
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$TargetPort = 15540,
    [int]$LegacyPort = 18001,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$project = "p18-$RunId"
$targetImage = 'redis/redisinsight:3.8.0@sha256:b5e19ee240abef6edb435871b90ff8a210995422e8e018ab61c0339d318a1f84'
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\redis-insight-rehearsal\$RunId"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$envFile = Join-Path $outputDirectory 'rehearsal.env'
$overlay = Join-Path $outputDirectory 'rehearsal-overlay.yml'
$targetUri = "http://127.0.0.1:$TargetPort"
$legacyUri = "http://127.0.0.1:$LegacyPort"
$results = New-Object System.Collections.Generic.List[object]
$runStarted = (Get-Date).ToUniversalTime()

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function New-Secret { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

function Invoke-Compose {
    $arguments = @('compose', '-p', $project, '--env-file', $envFile, '-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml', '-f', $overlay) + $args
    & docker @arguments
}

# Commands travel base64-encoded (no byte-order mark from Windows PowerShell 5.1 stdin, no quoting issues) and the
# password goes through REDISCLI_AUTH, never on a command line or in a log.
function Invoke-Redis([string[]]$Commands) {
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((($Commands -join "`n") + "`n")))
    $output = Invoke-Compose exec -T -e "P18_COMMANDS=$encoded" -e "REDISCLI_AUTH=$redisPassword" redis sh -c 'echo $P18_COMMANDS | base64 -d | redis-cli --no-auth-warning' 2>&1
    return @($output | ForEach-Object { "$_".TrimEnd() })
}

function Invoke-Api([string]$BaseUri, [string]$Method, [string]$Path, $Body) {
    try {
        $parameters = @{ Uri = "$BaseUri$Path"; Method = $Method; UseBasicParsing = $true; TimeoutSec = 30 }
        if ($null -ne $Body) { $parameters.Body = ($Body | ConvertTo-Json -Depth 5 -Compress); $parameters.ContentType = 'application/json' }
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = [string]$response.Content; ContentType = [string]$response.Headers['Content-Type'] }
    }
    catch {
        $status = 0; $content = ''
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            # Windows PowerShell 5.1 has already read the error body into ErrorDetails.
            $content = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { '' }
        }
        return [pscustomobject]@{ Status = $status; Content = $content; ContentType = '' }
    }
}

# Windows PowerShell 5.1 emits a JSON array as one object; enumerate it explicitly.
function Get-Databases {
    $parsed = (Invoke-Api $targetUri 'GET' '/api/databases' $null).Content | ConvertFrom-Json
    return @($parsed | ForEach-Object { $_ })
}

function Wait-Healthy([string]$Service, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $state = (Invoke-Compose ps -a --format '{{.Service}}|{{.State}}|{{.Health}}' 2>$null) | Where-Object { $_ -like "$Service|*" }
        if ($state -eq "$Service|running|healthy") { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Get-ContainerName([string]$Service) { return ((Invoke-Compose ps -a -q $Service 2>$null) | Select-Object -First 1) }

function Get-VolumeDigest([string]$Volume) {
    # Read-only mount in a throwaway container of the pinned Redis image (sha256sum/find available); content hash only.
    $digest = docker run --rm -v "${Volume}:/v:ro" --entrypoint sh $redisImage -c 'cd /v && find . -type f -exec sha256sum {} + | sort -k2 | sha256sum' 2>&1
    return (($digest | Out-String).Trim() -split '\s+')[0]
}

function Get-RedisSnapshot {
    # Values (DUMP of every key in sorted order), key count and index list: anything Redis Insight could have changed.
    $keys = @(Invoke-Compose exec -T -e "REDISCLI_AUTH=$redisPassword" redis redis-cli --no-auth-warning --scan 2>&1 | ForEach-Object { "$_".Trim() } | Where-Object { $_ } | Sort-Object)
    $dump = Invoke-Redis (@('DBSIZE', 'FT._LIST') + @($keys | ForEach-Object { "DUMP $_" }))
    $text = "keys=$($keys -join ',')`n" + ($dump -join "`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) | ForEach-Object { $_.ToString('x2') })
    return [pscustomobject]@{ Keys = $keys; Hash = $hash }
}

function Get-NormalVolumes {
    $names = docker volume ls --format '{{.Name}}' | Where-Object { $_ -like 'cleanarchitecture_*' -or $_ -like 'templatecleanarchnet_*' }
    return ($names | ForEach-Object { "$_@" + (docker volume inspect $_ --format '{{.CreatedAt}}') } | Sort-Object) -join ';'
}

# ---------------------------------------------------------------- 0. Isolation guard, synthetic environment, overlay
$existing = docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Names}}'
$existingVolumes = docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}'
if ($existing -or $existingVolumes) { throw "Compose project $project already has resources; choose another RunId." }
$normalVolumesBefore = Get-NormalVolumes

$redisPassword = New-Secret
$envLines = Get-Content .env.example | ForEach-Object {
    if ($_ -match '^(\w+)=CHANGE_ME$') { "$($Matches[1])=$(if ($Matches[1] -eq 'REDIS_PASSWORD') { $redisPassword } else { New-Secret })" }
    elseif ($_ -match '^APPDATA=') { 'APPDATA=/nonexistent' }
    elseif ($_ -match '^(\w+)_PORT=') { $null }
    else { $_ }
}
$envLines += 'P14_WEBAPI_PORT=1'
$envLines | Set-Content -Path $envFile -Encoding ascii

# The legacy service reuses exactly the image, environment and volume of the committed rollback overlay.
$rollbackJson = docker compose --env-file $envFile -f docker-compose.yml -f docker-compose.redis-insight-rollback.yml config --format json 2>$null | Out-String | ConvertFrom-Json
$legacy = $rollbackJson.services.'redis-insight'
$legacyImage = [string]$legacy.image
$legacyMount = @($legacy.volumes | ForEach-Object { "$($_.source):$($_.target)" }) -join ','
Write-Check 'rollback-overlay.legacy-image-1.14.0' ($legacyImage -eq 'redislabs/redisinsight:1.14.0@sha256:b03ab1426d0d861b0370e05ee670a8819c8ab36b987ca59e4a3e92e0038d089c') $legacyImage
Write-Check 'rollback-overlay.legacy-volume' ($legacyMount -eq 'redis-insight-volume_data:/db' -and $legacy.environment.RIPORT -eq '5540') $legacyMount
@"
services:
  redis-insight:
    ports:
      - "127.0.0.1:$($TargetPort):5540"
  redis-insight-legacy:
    image: $legacyImage
    environment:
      RIPORT: "5540"
    depends_on:
      - redis
    networks:
      - nw
    volumes:
      - redis-insight-volume_data:/db
    ports:
      - "127.0.0.1:$($LegacyPort):5540"
"@ | Set-Content -Path $overlay -Encoding ascii
Write-Check 'isolation.project-is-new' $true "project=$project"

# ---------------------------------------------------------------- 1. Static checks: image, digest, Compose shape
$configJson = Invoke-Compose config --format json 2>$null | Out-String | ConvertFrom-Json
$redisImage = [string]$configJson.services.redis.image
$target = $configJson.services.'redis-insight'
Write-Check 'target.compose-image-pinned' ([string]$target.image -eq $targetImage) ([string]$target.image)
Write-Check 'target.compose-volume-new-only' ((@($target.volumes | ForEach-Object { "$($_.source):$($_.target)" }) -join ',') -eq 'redis-insight-data:/data') ''
Write-Check 'target.depends-on-redis-only' ((@($target.depends_on.PSObject.Properties.Name) -join ',') -eq 'redis') ''
$dependents = @($configJson.services.PSObject.Properties | Where-Object { $_.Name -ne 'redis-insight-legacy' -and $_.Value.depends_on -and @($_.Value.depends_on.PSObject.Properties.Name | Where-Object { $_ -like 'redis-insight*' }).Count -gt 0 } | ForEach-Object { $_.Name })
Write-Check 'target.no-service-depends-on-insight' ($dependents.Count -eq 0) ($dependents -join ',')
$removedServices = @($configJson.services.PSObject.Properties.Name | Where-Object { $_ -in @('redis-graph', 'redis-search', 'redis-timeseries') })
Write-Check 'target.no-removed-redis-services' ($removedServices.Count -eq 0) ($removedServices -join ',')
$registry = docker buildx imagetools inspect redis/redisinsight:3.8.0 2>&1 | Out-String
$registryDigest = if ($registry -match 'Digest:\s+(sha256:[0-9a-f]{64})') { $Matches[1] } else { 'unknown' }
Write-Check 'target.registry-tag-matches-pin' ($targetImage.EndsWith("@$registryDigest")) $registryDigest
Write-Check 'target.registry-has-linux-amd64' ($registry -match 'Platform:\s+linux/amd64') ''

# ---------------------------------------------------------------- 2. Redis 8 with synthetic data
Invoke-Compose up -d --wait --wait-timeout $TimeoutSeconds redis 2>&1 | Set-Content (Join-Path $outputDirectory '01-redis-up.log')
Write-Check 'redis.healthy' ($LASTEXITCODE -eq 0) ''
$productIds = 1..5 | ForEach-Object { '00000000-0000-4000-8000-00000000001' + $_ }
$seed = @()
foreach ($id in $productIds) {
    $data = '{"id":"' + $id + '","name":"P18Synthetic","price":1.5,"barCode":"P18","createdDateTime":"2026-10-12T00:00:00Z"}'
    $seed += "HSET template:product:v1:$id absexp 639000000000000000 sldexp 1200000000 data '$data'"
    $seed += "EXPIRE template:product:v1:$id 43200"
}
$seed += 'HSET p18:search:product:1 name widget price 10 category odd'
$seed += 'HSET p18:search:product:2 name gadget price 20 category even'
$seed += 'HSET p18:search:product:3 name widget-pro price 30 category odd'
$seed += 'FT.CREATE idx:p18:product ON HASH PREFIX 1 p18:search:product: SCHEMA name TEXT SORTABLE price NUMERIC SORTABLE category TAG'
$seed += 'TS.CREATE p18:ts:raw RETENTION 31536000000 DUPLICATE_POLICY LAST LABELS phase 18 kind raw'
$seed += 'TS.CREATE p18:ts:avg LABELS phase 18 kind avg'
$seed += 'TS.CREATERULE p18:ts:raw p18:ts:avg AGGREGATION avg 60000'
$seed += 'TS.ADD p18:ts:raw 1000 10'
$seed += 'TS.ADD p18:ts:raw 2000 30'
$seedOutput = Invoke-Redis $seed
$seedErrors = @($seedOutput | Where-Object { $_ -match '^(ERR|WRONGTYPE|NOAUTH)' })
Write-Check 'redis.synthetic-data-seeded' ($seedErrors.Count -eq 0) ($seedErrors -join ' | ')
$version = (Invoke-Redis @('INFO server') | Where-Object { $_ -like 'redis_version:*' }) -replace 'redis_version:', ''
Write-Check 'redis.runtime-8.10.2' ($version -eq '8.10.2') "version=$version"
$snapshot0 = Get-RedisSnapshot
Write-Check 'redis.synthetic-key-count' ($snapshot0.Keys.Count -eq 10) "keys=$($snapshot0.Keys.Count)"

# ---------------------------------------------------------------- 3. Legacy 1.14 with a synthetic saved connection
Invoke-Compose up -d redis-insight-legacy 2>&1 | Set-Content (Join-Path $outputDirectory '02-legacy-up.log')
$legacyReady = $false
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $legacyReady -and (Get-Date) -lt $deadline) { $legacyReady = (Invoke-Api $legacyUri 'GET' '/healthcheck/' $null).Content -eq 'OK'; if (-not $legacyReady) { Start-Sleep -Seconds 2 } }
Write-Check 'legacy.healthcheck' $legacyReady ''
$legacyAdd = Invoke-Api $legacyUri 'POST' '/api/instance/' @{ name = 'p18-legacy-synthetic'; connectionType = 'STANDALONE'; host = 'redis'; port = 6379; password = $redisPassword }
Write-Check 'legacy.synthetic-connection-saved' ($legacyAdd.Status -eq 200 -or $legacyAdd.Status -eq 201) "status=$($legacyAdd.Status)"
# The 1.x API returns saved passwords in clear text; only names and host:port leave this function.
$legacyList = Invoke-Api $legacyUri 'GET' '/api/instance/' $null
$legacyConnections = @(($legacyList.Content | ConvertFrom-Json).dbs | ForEach-Object { "$($_.name)@$($_.host):$($_.port)" })
Write-Check 'legacy.inventory' (($legacyConnections -join ',') -eq 'p18-legacy-synthetic@redis:6379') ($legacyConnections -join ',')
Write-Check 'legacy.api-exposes-saved-password' ($legacyList.Content.Contains($redisPassword)) 'security finding of the 1.x line (value not recorded)'
Invoke-Compose stop redis-insight-legacy 2>&1 | Out-Null
$legacyVolume = "${project}_redis-insight-volume_data"
$legacyDigestBefore = Get-VolumeDigest $legacyVolume
Write-Check 'legacy.stopped-volume-digest' ($legacyDigestBefore -match '^[0-9a-f]{64}$') "volume=$legacyVolume"

# ---------------------------------------------------------------- 4. Target 3.8.0 on the new volume
$targetStarted = Get-Date
Invoke-Compose up -d --no-deps redis-insight 2>&1 | Set-Content (Join-Path $outputDirectory '03-target-up.log')
$healthy = Wait-Healthy 'redis-insight' $TimeoutSeconds
Write-Check 'target.healthy-within-bound' $healthy ("seconds=" + [int]((Get-Date) - $targetStarted).TotalSeconds + " bound=$TimeoutSeconds")
$targetContainer = Get-ContainerName 'redis-insight'
$targetImageId = docker inspect $targetContainer --format '{{.Image}}'
$pinnedImageId = docker image inspect $targetImage --format '{{.Id}}'
Write-Check 'target.container-runs-pinned-image' ($targetImageId -eq $pinnedImageId) ''
$mounts = docker inspect $targetContainer --format '{{range .Mounts}}{{.Name}}:{{.Destination}} {{end}}'
$targetVolume = "${project}_redis-insight-data"
Write-Check 'target.mounts-only-new-volume' ($mounts.Trim() -eq "${targetVolume}:/data") ($mounts.Trim())
$volumeCreated = [datetime](docker volume inspect $targetVolume --format '{{.CreatedAt}}')
Write-Check 'target.volume-created-in-this-run' ($volumeCreated.ToUniversalTime() -ge $runStarted.AddSeconds(-5)) ''
$user = docker inspect $targetContainer --format '{{.Config.User}}'
Write-Check 'target.runs-as-documented-user' ($user -eq 'node') "user=$user"

$health = Invoke-Api $targetUri 'GET' '/api/health/' $null
Write-Check 'target.api-health-up' ($health.Status -eq 200 -and $health.Content -match '"status":"up"') ''
$info = (Invoke-Api $targetUri 'GET' '/api/info' $null).Content | ConvertFrom-Json
Write-Check 'target.runtime-version-3.8.0' ($info.appVersion -eq '3.8.0' -and $info.appType -eq 'DOCKER') "version=$($info.appVersion) build=$($info.buildType)"
Write-Check 'target.key-encryption-available' (@($info.encryptionStrategies) -contains 'KEY') (@($info.encryptionStrategies) -join ',')
$index = Invoke-Api $targetUri 'GET' '/' $null
Write-Check 'ui.index-html' ($index.Status -eq 200 -and $index.Content -match '<div id="root"') "status=$($index.Status)"
$assets = @([regex]::Matches($index.Content, '(?:src|href)="([^"]+\.(?:js|css))"') | ForEach-Object { $_.Groups[1].Value })
$assetStatuses = @($assets | Select-Object -First 4 | ForEach-Object { $path = if ($_.StartsWith('/')) { $_ } else { '/' + $_.TrimStart('.', '/') }; (Invoke-Api $targetUri 'GET' $path $null).Status })
Write-Check 'ui.static-assets' ($assetStatuses.Count -gt 0 -and @($assetStatuses | Where-Object { $_ -ne 200 }).Count -eq 0) ("assets=" + ($assets | Select-Object -First 4) -join ',')

# Before the EULA: no agreements and no connection (Redis Insight 3.x creates pre-setup connections only afterwards).
$settings = (Invoke-Api $targetUri 'GET' '/api/settings' $null).Content | ConvertFrom-Json
$databasesBefore = @(Get-Databases)
Write-Check 'target.eula-not-auto-accepted' ($null -eq $settings.agreements -and $databasesBefore.Count -eq 0) "connections=$($databasesBefore.Count)"
$accept = Invoke-Api $targetUri 'PATCH' '/api/settings' @{ agreements = @{ eula = $true; analytics = $false; notifications = $false; encryption = $true }; theme = 'DARK' }
Write-Check 'target.eula-accepted-in-rehearsal-only' ($accept.Status -eq 200) 'analytics=false notifications=false encryption=true'

# ---------------------------------------------------------------- 5. Connection recreated from RI_REDIS_* (no secret in source)
$connection = $null
$deadline = (Get-Date).AddSeconds(30)
while (-not $connection -and (Get-Date) -lt $deadline) {
    $connection = @(Get-Databases) | Where-Object { $_.name -eq 'template-redis8' }
    if (-not $connection) { Start-Sleep -Seconds 2 }
}
$allConnections = @(Get-Databases)
Write-Check 'connection.recreated-single' ($allConnections.Count -eq 1 -and $connection) (($allConnections | ForEach-Object { "$($_.name)@$($_.host):$($_.port)/db$($_.db)" }) -join ',')
Write-Check 'connection.no-legacy-or-graph-connections' (@($allConnections | Where-Object { $_.name -like '*legacy*' -or $_.host -match 'graph|search|timeseries' }).Count -eq 0) ''
$id = [string]$connection.id
$details = (Invoke-Api $targetUri 'GET' "/api/databases/$id" $null).Content | ConvertFrom-Json
Write-Check 'connection.target-redis-6379-db0-no-tls' ($details.host -eq 'redis' -and $details.port -eq 6379 -and $details.db -eq 0 -and $details.tls -ne $true) ''
Write-Check 'connection.password-not-returned' ($details.password -eq $true -and -not ((Invoke-Api $targetUri 'GET' "/api/databases/$id" $null).Content.Contains($redisPassword))) ''
$connect = Invoke-Api $targetUri 'GET' "/api/databases/$id/connect" $null
Write-Check 'connection.connects-with-default-user-auth' ($connect.Status -eq 200) "status=$($connect.Status)"
$details = (Invoke-Api $targetUri 'GET' "/api/databases/$id" $null).Content | ConvertFrom-Json
$modules = @($details.modules | ForEach-Object { $_.name } | Sort-Object)
Write-Check 'connection.standalone-redis-8.10.2' ($details.connectionType -eq 'STANDALONE' -and $details.version -eq '8.10.2') "type=$($details.connectionType) version=$($details.version)"
Write-Check 'connection.search-and-timeseries-no-graph' ($modules -contains 'search' -and $modules -contains 'timeseries' -and -not ($modules -match 'graph')) ($modules -join ',')

# ---------------------------------------------------------------- 6. Bounded, read-only inspection (no payloads recorded)
$cacheScan = (Invoke-Api $targetUri 'POST' "/api/databases/$id/keys?encoding=utf8" @{ cursor = '0'; count = 100; match = 'template:product:v1:*' }).Content | ConvertFrom-Json
$cacheKeys = @($cacheScan | ForEach-Object { $_.keys } | Where-Object { $_ })
Write-Check 'inspect.cache-namespace-bounded-scan' ($cacheKeys.Count -eq 5 -and @($cacheKeys | Where-Object { $_.type -ne 'hash' -or $_.ttl -le 0 }).Count -eq 0) "keys=$($cacheKeys.Count) types=hash ttl>0"
$cacheInfo = (Invoke-Api $targetUri 'POST' "/api/databases/$id/keys/get-info?encoding=utf8" @{ keyName = "template:product:v1:$($productIds[0])" }).Content | ConvertFrom-Json
Write-Check 'inspect.cache-key-ttl-metadata' ($cacheInfo.type -eq 'hash' -and $cacheInfo.ttl -gt 0 -and $cacheInfo.ttl -le 43200 -and $cacheInfo.length -eq 3) "ttl=$($cacheInfo.ttl) fields=$($cacheInfo.length)"
$indexes = ((Invoke-Api $targetUri 'GET' "/api/databases/$id/redisearch?encoding=utf8" $null).Content | ConvertFrom-Json).indexes
Write-Check 'inspect.search-index-listed' (@($indexes) -contains 'idx:p18:product') (@($indexes) -join ',')
$indexInfo = (Invoke-Api $targetUri 'POST' "/api/databases/$id/redisearch/info" @{ index = 'idx:p18:product' }).Content | ConvertFrom-Json
Write-Check 'inspect.search-index-info' ($indexInfo.index_name -eq 'idx:p18:product' -and [int]$indexInfo.num_docs -eq 3 -and @($indexInfo.attributes).Count -eq 3) "docs=$($indexInfo.num_docs) attributes=$(@($indexInfo.attributes).Count)"
$tsScan = (Invoke-Api $targetUri 'POST' "/api/databases/$id/keys?encoding=utf8" @{ cursor = '0'; count = 100; match = 'p18:ts:*' }).Content | ConvertFrom-Json
$tsKeys = @($tsScan | ForEach-Object { $_.keys } | Where-Object { $_ })
Write-Check 'inspect.timeseries-keys-typed' ($tsKeys.Count -eq 2 -and @($tsKeys | Where-Object { $_.type -ne 'TSDB-TYPE' }).Count -eq 0) "keys=$($tsKeys.Count)"
$tsInfo = (Invoke-Api $targetUri 'POST' "/api/databases/$id/keys/get-info?encoding=utf8" @{ keyName = 'p18:ts:raw' }).Content | ConvertFrom-Json
Write-Check 'inspect.timeseries-metadata' ($tsInfo.type -eq 'TSDB-TYPE' -and $tsInfo.ttl -eq -1 -and $tsInfo.length -eq 2) "ttl=$($tsInfo.ttl) samples=$($tsInfo.length)"
$overview = (Invoke-Api $targetUri 'GET' "/api/databases/$id/overview" $null).Content | ConvertFrom-Json
Write-Check 'inspect.overview' ($overview.version -eq '8.10.2' -and [int]$overview.totalKeys -eq 10) "keys=$($overview.totalKeys)"
$snapshot1 = Get-RedisSnapshot
Write-Check 'inspect.redis-data-unchanged' ($snapshot1.Hash -eq $snapshot0.Hash) ''

# ---------------------------------------------------------------- 7. Authentication failure is safe
$wrongValue = 'p18-wrong-' + (New-Secret)
$wrong = Invoke-Api $targetUri 'POST' '/api/databases/test' @{ name = 'p18-wrong-auth'; host = 'redis'; port = 6379; password = $wrongValue }
Write-Check 'auth.wrong-password-rejected' ($wrong.Status -eq 424 -and $wrong.Content -match 'Failed to authenticate' -and -not $wrong.Content.Contains($wrongValue)) "status=$($wrong.Status)"
Write-Check 'auth.wrong-password-not-saved' (@(Get-Databases).Count -eq 1) ''

# ---------------------------------------------------------------- 8. Restart persistence
Invoke-Compose restart redis-insight 2>&1 | Out-Null
Write-Check 'restart.healthy-again' (Wait-Healthy 'redis-insight' $TimeoutSeconds) ''
$settingsAfter = (Invoke-Api $targetUri 'GET' '/api/settings' $null).Content | ConvertFrom-Json
Write-Check 'restart.agreements-and-settings-persisted' ($settingsAfter.agreements.eula -eq $true -and $settingsAfter.agreements.analytics -eq $false -and $settingsAfter.theme -eq 'DARK') ''
$afterRestart = @(Get-Databases)
Write-Check 'restart.connection-persisted' ($afterRestart.Count -eq 1 -and [string]$afterRestart[0].id -eq $id -and $afterRestart[0].name -eq 'template-redis8') ''
Write-Check 'restart.connects-again' ((Invoke-Api $targetUri 'GET' "/api/databases/$id/connect" $null).Status -eq 200) ''

# ---------------------------------------------------------------- 9. Secrets: not in the target volume, logs or evidence
$secretHits = Invoke-Compose exec -T -e "P18_SECRET=$redisPassword" -e "P18_WRONG=$wrongValue" redis-insight sh -c 'grep -rlF -e "$P18_SECRET" -e "$P18_WRONG" /data | wc -l' 2>&1 | Out-String
Write-Check 'secrets.not-stored-in-clear-in-target-volume' ($secretHits.Trim() -eq '0') "files=$($secretHits.Trim())"
$targetLogs = Invoke-Compose logs --no-color redis-insight 2>&1 | Out-String
Write-Check 'secrets.not-in-container-logs' (-not $targetLogs.Contains($redisPassword) -and -not $targetLogs.Contains($wrongValue)) ''
$targetLogs -split "`n" | Select-Object -Last 60 | Set-Content (Join-Path $outputDirectory '04-target-tail.log')

# ---------------------------------------------------------------- 10. Target absent: Redis unaffected
Invoke-Compose stop redis-insight 2>&1 | Out-Null
$states = (Invoke-Compose ps -a --format '{{.Service}}|{{.State}}' 2>$null | Sort-Object) -join ';'
Write-Check 'absent.target-stopped-redis-running' ($states -match 'redis-insight\|exited' -and $states -match '(^|;)redis\|running') $states
$ping = (Invoke-Redis @('PING')) -join ''
Write-Check 'absent.redis-ping' ($ping -eq 'PONG') $ping
$snapshot2 = Get-RedisSnapshot
Write-Check 'absent.redis-data-unchanged' ($snapshot2.Hash -eq $snapshot0.Hash) ''
$legacyDigestAfterTarget = Get-VolumeDigest $legacyVolume
Write-Check 'legacy.volume-untouched-by-target' ($legacyDigestAfterTarget -eq $legacyDigestBefore) ''

# ---------------------------------------------------------------- 11. Rollback: legacy 1.14 on its untouched volume
$rollbackStarted = Get-Date
Invoke-Compose up -d --no-deps redis-insight-legacy 2>&1 | Set-Content (Join-Path $outputDirectory '05-rollback-up.log')
$legacyReady = $false
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $legacyReady -and (Get-Date) -lt $deadline) { $legacyReady = (Invoke-Api $legacyUri 'GET' '/healthcheck/' $null).Content -eq 'OK'; if (-not $legacyReady) { Start-Sleep -Seconds 1 } }
Write-Check 'rollback.legacy-healthy' $legacyReady ("seconds=" + [int]((Get-Date) - $rollbackStarted).TotalSeconds)
Write-Check 'rollback.legacy-ui' ((Invoke-Api $legacyUri 'GET' '/' $null).Status -eq 200) ''
$legacyAfter = @(((Invoke-Api $legacyUri 'GET' '/api/instance/' $null).Content | ConvertFrom-Json).dbs | ForEach-Object { "$($_.name)@$($_.host):$($_.port)" })
Write-Check 'rollback.saved-connection-preserved' (($legacyAfter -join ',') -eq ($legacyConnections -join ',')) ($legacyAfter -join ',')
$legacyContainer = Get-ContainerName 'redis-insight-legacy'
$legacyMounts = docker inspect $legacyContainer --format '{{range .Mounts}}{{.Name}}:{{.Destination}} {{end}}'
Write-Check 'rollback.legacy-mounts-only-legacy-volume' ($legacyMounts.Trim() -eq "${legacyVolume}:/db") ($legacyMounts.Trim())
Write-Check 'rollback.redis-data-unchanged' ((Get-RedisSnapshot).Hash -eq $snapshot0.Hash) ''

# ---------------------------------------------------------------- 12. Non-destructive end
Invoke-Compose stop 2>&1 | Out-Null
$states = (Invoke-Compose ps -a --format '{{.Service}}|{{.State}}' 2>$null | Sort-Object) -join ';'
Write-Check 'shutdown.all-stopped' ($states -notmatch '\|running') $states
$volumes = (docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}' | Sort-Object) -join ','
Write-Check 'shutdown.volumes-retained' ($volumes -match [regex]::Escape($targetVolume) -and $volumes -match [regex]::Escape($legacyVolume)) $volumes
Write-Check 'isolation.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''

$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$evidence = Get-ChildItem $outputDirectory -File | Where-Object { $_.Name -ne 'rehearsal.env' } | ForEach-Object { Get-Content $_.FullName -Raw }
Write-Check 'secrets.not-in-evidence' (-not (($evidence -join "`n").Contains($redisPassword))) ''
$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$failed = @($results | Where-Object { -not $_.Passed })
"project=$project checks=$($results.Count) failed=$($failed.Count)" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
