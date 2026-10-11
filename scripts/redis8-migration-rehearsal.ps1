<#
.SYNOPSIS
    Phase 15 isolated Redis migration rehearsal: the preserved pre-Phase 15 Redis topology (Redis 8.4.0 + RediSearch
    2.6.13 + RedisTimeSeries 1.6.19 + RedisGraph 2.10.16 on Redis 6.2) -> one Redis 8.10.2 server on a new volume.
.DESCRIPTION
    Source: new Compose project p15-src-<RunId> rendered from docker-compose.yml + docker-compose.baseline.yml +
    docker-compose.redis-rollback.yml (exactly the rollback configuration), started with only its Redis services.
    Target: new Compose project p15-tgt-<RunId> rendered from docker-compose.yml + docker-compose.baseline.yml with
    the redis service published on a loopback port, on its own new redis8-data volume.
    Data is synthetic only (no user Redis data is read). Transfer follows the official mechanisms:
      * main server: RDB snapshot (SAVE) copied into the new, empty target volume before the target first starts;
      * module servers (no named volume in the source topology): MIGRATE ... COPY (DUMP/RESTORE, keeps TTLs and
        leaves the source untouched); Search index definitions are re-created with FT.CREATE, which re-indexes the
        migrated hashes.
    The source and target never share a volume, and the source server never opens target data. Every RDB is kept
    as a recovery artifact. The script never removes containers, volumes, images, or networks; it ends with `stop`.
    Synthetic passwords live only in an untracked env file under artifacts/validation/redis8-rehearsal/<RunId>.
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$TargetPort = 16379
)

$ErrorActionPreference = 'Continue'
$script:unexpectedRedisErrors = New-Object System.Collections.Generic.List[string]
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$sourceProject = "p15-src-$RunId"
$targetProject = "p15-tgt-$RunId"
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\redis8-rehearsal\$RunId"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$envFile = Join-Path $outputDirectory 'rehearsal.env'
$targetOverlay = Join-Path $outputDirectory 'target-port.yml'
$results = New-Object System.Collections.Generic.List[object]
$targetImage = 'redis:8.10.2@sha256:2c2dff791878316e3b083188be0d578423b24813cdda5de0e80ea4f7b3e6bfd6'
$sourceImage = 'redis:8.4.0@sha256:47200b04138293fae39737e50878a238b13ec0781083126b1b0c63cf5d992e8d'

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function Invoke-Source {
    $arguments = @('compose', '-p', $sourceProject, '--env-file', $envFile, '-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml', '-f', 'docker-compose.redis-rollback.yml') + $args
    & docker @arguments
}

function Invoke-Target {
    $arguments = @('compose', '-p', $targetProject, '--env-file', $envFile, '-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml', '-f', $targetOverlay) + $args
    & docker @arguments
}

# Commands use redis-cli inline syntax and reach redis-cli's stdin inside the container from a base64 exec variable:
# JSON quotes survive Windows PowerShell 5.1 argument passing, no byte-order mark is injected (5.1 adds one when piping
# to native stdin), and the password goes through REDISCLI_AUTH, never on a command line or in a log.
function Invoke-Redis([string]$Side, [string]$Service, [string[]]$Commands, [switch]$NoAuth) {
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((($Commands -join "`n") + "`n")))
    $exec = @('exec', '-T', '-e', "P15_COMMANDS=$encoded")
    if (-not $NoAuth -and $Service -eq 'redis') { $exec += @('-e', "REDISCLI_AUTH=$redisPassword") }
    $exec += @($Service, 'sh', '-c', 'echo $P15_COMMANDS | base64 -d | redis-cli --no-auth-warning')
    $files = if ($Side -eq 'source') { @('-p', $sourceProject, '-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml', '-f', 'docker-compose.redis-rollback.yml') }
             else { @('-p', $targetProject, '-f', 'docker-compose.yml', '-f', 'docker-compose.baseline.yml', '-f', $targetOverlay) }
    $arguments = @('compose', '--env-file', $envFile) + $files + $exec
    $output = @(& docker @arguments 2>&1 | ForEach-Object { "$_".TrimEnd() })
    # Unknown commands, syntax and wrong-type errors are never expected (ACL denials and NOAUTH are checked explicitly).
    $output | Where-Object { $_ -match '^(ERR unknown|ERR syntax|ERR wrong|WRONGTYPE|ERR Error parsing)' } | ForEach-Object { $script:unexpectedRedisErrors.Add("$Side/$Service`: $_") }
    return $output
}

function Get-Hash([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text)) | ForEach-Object { $_.ToString('x2') })
}

function Get-NormalVolumes {
    $names = docker volume ls --format '{{.Name}}' | Where-Object { $_ -like 'cleanarchitecture_*' -or $_ -like 'templatecleanarchnet_*' }
    return ($names | ForEach-Object { "$_@" + (docker volume inspect $_ --format '{{.CreatedAt}}') } | Sort-Object) -join ';'
}

function Get-Value([string[]]$Lines, [string]$Field) {
    $index = [Array]::IndexOf($Lines, $Field)
    if ($index -ge 0 -and $index + 1 -lt $Lines.Count) { return $Lines[$index + 1] }
    return $null
}

# Semantic state of the synthetic data on one server (no values are printed; hashes are compared by digest).
function Get-State([string]$Side, [string]$Service, [string]$Pattern) {
    # KEYS is acceptable here: isolated rehearsal servers holding a dozen synthetic keys.
    $keys = @(Invoke-Redis $Side $Service @("KEYS '$Pattern'") | Where-Object { $_ } | Sort-Object)
    $rows = foreach ($key in $keys) {
        $type = @(Invoke-Redis $Side $Service @("TYPE '$key'"))[0]
        $ttl = [long](@(Invoke-Redis $Side $Service @("PTTL '$key'"))[0])
        $digest = switch ($type) {
            'hash' { Get-Hash ((Invoke-Redis $Side $Service @("HGETALL '$key'") | Sort-Object) -join "`n") }
            'TSDB-TYPE' { Get-Hash ((Invoke-Redis $Side $Service @("TS.RANGE '$key' - +")) -join ',') }
            default { $type }
        }
        [pscustomobject]@{ Key = $key; Type = $type; Ttl = $ttl; Digest = $digest }
    }
    return @($rows)
}

function Compare-State([object[]]$Expected, [object[]]$Actual, [double]$ElapsedMs) {
    if ($Expected.Count -eq 0) { return 'no expected keys' }
    if ($Expected.Count -ne $Actual.Count) { return "count expected=$($Expected.Count) actual=$($Actual.Count)" }
    for ($i = 0; $i -lt $Expected.Count; $i++) {
        $e = $Expected[$i]; $a = $Actual[$i]
        if ($e.Key -ne $a.Key -or $e.Type -ne $a.Type -or $e.Digest -ne $a.Digest) { return "mismatch key=$($e.Key)" }
        if ($e.Ttl -lt 0 -and $a.Ttl -ne $e.Ttl) { return "ttl key=$($e.Key) expected=$($e.Ttl) actual=$($a.Ttl)" }
        if ($e.Ttl -ge 0 -and ($a.Ttl -le 0 -or $a.Ttl -gt $e.Ttl -or ($e.Ttl - $a.Ttl) -gt ($ElapsedMs + 5000))) { return "ttl key=$($e.Key) expected<=$($e.Ttl) actual=$($a.Ttl)" }
    }
    return ''
}

# A terminating error must never leave rehearsal servers running: stop both projects (non-destructive) and rethrow.
trap {
    Invoke-Source stop 2>&1 | Out-Null
    Invoke-Target stop 2>&1 | Out-Null
    break
}

# ---------------------------------------------------------------- 0. Isolation guard and synthetic environment
foreach ($project in @($sourceProject, $targetProject)) {
    $existing = docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Names}}'
    $existingVolumes = docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}'
    if ($existing -or $existingVolumes) { throw "Compose project $project already has resources; choose another RunId." }
}
$normalVolumesBefore = Get-NormalVolumes
function New-Secret { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }
$redisPassword = New-Secret
$envLines = Get-Content .env.example | ForEach-Object {
    if ($_ -match '^(\w+)=CHANGE_ME$') { "$($Matches[1])=$(if ($Matches[1] -eq 'REDIS_PASSWORD') { $redisPassword } else { New-Secret })" }
    elseif ($_ -match '^APPDATA=') { 'APPDATA=/nonexistent' }
    elseif ($_ -match '^(\w+)_PORT=') { $null }
    else { $_ }
}
$envLines += @('P14_WEBAPI_PORT=18099', "P15_TARGET_PORT=$TargetPort")
$envLines | Set-Content -Path $envFile -Encoding ascii
@(
    '# Rehearsal-only overlay: publishes the target redis on loopback so the source module servers can MIGRATE into it.'
    'services:'
    '  redis:'
    '    ports:'
    '      - "127.0.0.1:${P15_TARGET_PORT}:6379"'
) | Set-Content -Path $targetOverlay -Encoding ascii
Write-Check 'isolation.projects-are-new' $true "source=$sourceProject target=$targetProject"

# ---------------------------------------------------------------- 1. Source environment (rollback configuration)
$sourceServices = @('redis', 'redis-search', 'redis-timeseries', 'redis-graph')
Invoke-Source up -d --wait --wait-timeout 180 @sourceServices 2>&1 | Set-Content (Join-Path $outputDirectory '01-source-up.log')
Write-Check 'source.up-healthy' ($LASTEXITCODE -eq 0) ''
$inventory = foreach ($service in $sourceServices) {
    $info = Invoke-Redis 'source' $service @('INFO server')
    $version = "$(($info | Where-Object { $_ -like 'redis_version:*' }) -replace 'redis_version:', '')"
    $modules = @(Invoke-Redis 'source' $service @('MODULE LIST')) -join ' '
    $moduleNames = ([regex]::Matches($modules, 'name (\S+) ver (\d+)') | ForEach-Object { "$($_.Groups[1].Value)@$($_.Groups[2].Value)" }) -join ','
    if (-not $moduleNames) {
        $lines = @(Invoke-Redis 'source' $service @('MODULE LIST'))
        $moduleNames = (0..($lines.Count - 1) | Where-Object { $lines[$_] -eq 'name' } | ForEach-Object { "$($lines[$_ + 1])@$($lines[$_ + 3])" }) -join ','
    }
    $config = @(Invoke-Redis 'source' $service @('CONFIG GET save', 'CONFIG GET appendonly', 'CONFIG GET maxmemory-policy', 'CONFIG GET dir'))
    $requirePass = (@(Invoke-Redis 'source' $service @('PING') -NoAuth) -join '') -match 'NOAUTH'
    $users = @(Invoke-Redis 'source' $service @('ACL USERS')) -join ','
    "$service version=$version modules=[$moduleNames] save=[$($config[1])] appendonly=$($config[3]) maxmemory-policy=$($config[5]) dir=$($config[7]) auth-required=$requirePass acl-users=[$users]"
}
$inventory | Set-Content (Join-Path $outputDirectory '02-source-inventory.txt')
$inventory | ForEach-Object { Write-Host "  $_" }
Write-Check 'source.versions' (($inventory -join "`n") -match 'redis version=8\.4\.0' -and ($inventory -join "`n") -match 'redis-search version=6\.2' -and ($inventory -join "`n") -match 'redis-timeseries version=6\.2' -and ($inventory -join "`n") -match 'redis-graph version=6\.2') ''

# Graph: the source graph server is empty and has no graphs (no consumer creates any).
$graphKeys = @(Invoke-Redis 'source' 'redis-graph' @('DBSIZE'))[0]
$graphList = @(Invoke-Redis 'source' 'redis-graph' @('GRAPH.LIST')) -join ','
Write-Check 'source.redisgraph-has-no-data' ($graphKeys -eq '0') "dbsize=$graphKeys graph.list=[$graphList]"

# ---------------------------------------------------------------- 2. Synthetic data from the actual schemas/commands
$now = [DateTimeOffset]::UtcNow
$cacheIds = 1..5 | ForEach-Object { [guid]::NewGuid().ToString('D') }
$cacheCommands = foreach ($id in $cacheIds) {
    # Microsoft.Extensions.Caching.StackExchangeRedis hash layout (absexp/sldexp ticks, data) with the v1 JSON payload.
    $json = "{`"id`":`"$id`",`"name`":`"Synthetic product`",`"price`":12.34,`"barCode`":`"7890000000000`",`"createdDateTime`":`"2026-10-11T12:00:00Z`"}"
    "HSET 'template:product:v1:$id' absexp $($now.AddDays(1).UtcTicks) sldexp $([TimeSpan]::FromHours(12).Ticks) data '$json'"
    "PEXPIRE 'template:product:v1:$id' $(43200000 + (Get-Random -Maximum 1000))"
}
Invoke-Redis 'source' 'redis' $cacheCommands | Out-Null
$searchCommands = @('FT.CREATE idx:p15:product ON HASH PREFIX 1 p15:search:product: SCHEMA name TEXT SORTABLE price NUMERIC SORTABLE category TAG')
$searchCommands += 1..5 | ForEach-Object { "HSET p15:search:product:$_ name 'synthetic widget $_' price $($_ * 10) category $(if ($_ % 2) { 'odd' } else { 'even' })" }
$searchCommands += 'PEXPIRE p15:search:product:5 86400000'
Invoke-Redis 'source' 'redis-search' $searchCommands | Out-Null
$tsStart = 1760000040000 - (1760000040000 % 60000)
$tsCommands = @(
    'TS.CREATE p15:ts:sensor:raw RETENTION 31536000000 DUPLICATE_POLICY LAST LABELS sensor s1 kind raw',
    'TS.CREATE p15:ts:sensor:avg LABELS sensor s1 kind avg',
    'TS.CREATERULE p15:ts:sensor:raw p15:ts:sensor:avg AGGREGATION avg 60000',
    "TS.ADD p15:ts:sensor:raw $tsStart 10", "TS.ADD p15:ts:sensor:raw $($tsStart + 1000) 20", "TS.ADD p15:ts:sensor:raw $($tsStart + 1000) 30",
    "TS.ADD p15:ts:sensor:raw $($tsStart + 60000) 50", "TS.ADD p15:ts:sensor:raw $($tsStart + 61000) 70"
)
Invoke-Redis 'source' 'redis-timeseries' $tsCommands | Out-Null

function Get-SearchState([string]$Side, [string]$Service) {
    $odd = @(Invoke-Redis $Side $Service @("FT.SEARCH idx:p15:product '@category:{odd}' SORTBY price DESC LIMIT 0 10 NOCONTENT")) -join ','
    $all = @(Invoke-Redis $Side $Service @('FT.SEARCH idx:p15:product widget LIMIT 0 0'))[0]
    $range = @(Invoke-Redis $Side $Service @("FT.SEARCH idx:p15:product '@price:[15 35]' SORTBY price ASC NOCONTENT")) -join ','
    return "odd=[$odd] all=$all range=[$range]"
}

function Get-SeriesState([string]$Side, [string]$Service) {
    $info = @(Invoke-Redis $Side $Service @('TS.INFO p15:ts:sensor:raw'))
    $labels = @(Invoke-Redis $Side $Service @('TS.QUERYINDEX sensor=s1') | Sort-Object) -join ','
    $raw = @(Invoke-Redis $Side $Service @('TS.RANGE p15:ts:sensor:raw - +')) -join ','
    $avg = @(Invoke-Redis $Side $Service @('TS.RANGE p15:ts:sensor:avg - +')) -join ','
    $policy = "$(Get-Value $info 'duplicatePolicy')".ToLowerInvariant()
    $rules = @($info | Where-Object { $_ -eq 'p15:ts:sensor:avg' }).Count
    return "retention=$(Get-Value $info 'retentionTime') duplicatePolicy=$policy totalSamples=$(Get-Value $info 'totalSamples') rules->avg=$rules queryindex=[$labels] raw=[$raw] avg=[$avg]"
}

# TTL comparisons allow for the time elapsed since the start of the capture.
$capturedAt = Get-Date
$sourceCache = Get-State 'source' 'redis' 'template:product:v1:*'
$sourceSearchKeys = Get-State 'source' 'redis-search' 'p15:search:*'
$sourceSeriesKeys = Get-State 'source' 'redis-timeseries' 'p15:ts:*'
Start-Sleep -Seconds 1
$sourceSearch = Get-SearchState 'source' 'redis-search'
$sourceSeries = Get-SeriesState 'source' 'redis-timeseries'
@("cache=$($sourceCache.Count) keys", "search-keys=$($sourceSearchKeys.Count)", "series-keys=$($sourceSeriesKeys.Count)", "search: $sourceSearch", "series: $sourceSeries") |
    Set-Content (Join-Path $outputDirectory '03-source-state.txt')
($sourceCache + $sourceSearchKeys + $sourceSeriesKeys) | Select-Object Key, Type, Ttl | Format-Table -AutoSize | Out-String -Width 200 |
    Set-Content (Join-Path $outputDirectory '03-source-keys.txt')
Write-Check 'source.synthetic-data' ($sourceCache.Count -eq 5 -and $sourceSearchKeys.Count -eq 5 -and $sourceSeriesKeys.Count -eq 2 -and $sourceSearch -match 'all=5') "cache=$($sourceCache.Count) search=$($sourceSearchKeys.Count) series=$($sourceSeriesKeys.Count)"
Write-Check 'source.search-and-series-behavior' ($sourceSearch -match 'odd=\[3,p15:search:product:5,p15:search:product:3,p15:search:product:1\]' -and $sourceSeries -match 'duplicatePolicy=last' -and $sourceSeries -match 'rules->avg=1') "$sourceSearch | $sourceSeries"

# ---------------------------------------------------------------- 3. Recovery artifacts (RDB snapshots) and their verification
$artifacts = @{}
foreach ($service in @('redis', 'redis-search', 'redis-timeseries')) {
    Invoke-Redis 'source' $service @('SAVE') | Out-Null
    $dir = @(Invoke-Redis 'source' $service @('CONFIG GET dir'))[1]
    $file = Join-Path $outputDirectory "source-$service.rdb"
    $container = (Invoke-Source ps -q $service | Out-String).Trim()
    docker cp "${container}:$dir/dump.rdb" $file 2>&1 | Out-Null
    $artifacts[$service] = $file
    $sha = if (Test-Path $file) { (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'missing' }
    "$service $sha $((Get-Item $file -ErrorAction SilentlyContinue).Length) bytes" | Add-Content (Join-Path $outputDirectory '04-recovery-artifacts.txt')
}
$check = docker run --rm -v "$($outputDirectory):/in:ro" --entrypoint redis-check-rdb $sourceImage /in/source-redis.rdb 2>&1 | Out-String
Write-Check 'artifact.source-redis-rdb-valid' ($check -match 'RDB looks OK') ''
foreach ($service in @('redis-search', 'redis-timeseries')) {
    # A module RDB is verified by loading it into a throwaway server of the same source image (no volume reuse).
    $image = (Invoke-Source config --format json | Out-String | ConvertFrom-Json).services.$service.image
    $loadModules = if ($service -eq 'redis-search') { '--loadmodule /usr/lib/redis/modules/redisearch.so --loadmodule /usr/lib/redis/modules/rejson.so' } else { '--loadmodule /usr/lib/redis/modules/redistimeseries.so' }
    $verify = docker run --rm -v "$($outputDirectory):/in:ro" --entrypoint sh $image -c "mkdir -p /tmp/v && cp /in/source-$service.rdb /tmp/v/dump.rdb && (redis-server --dir /tmp/v $loadModules --daemonize yes >/dev/null) && sleep 2 && redis-cli DBSIZE && redis-cli shutdown nosave >/dev/null 2>&1; true" 2>&1 | Out-String
    $expected = if ($service -eq 'redis-search') { 5 } else { 2 }
    Write-Check "artifact.source-$service-rdb-loads" ($verify.Trim() -match "^$expected") "dbsize=$($verify.Trim())"
}

# ---------------------------------------------------------------- 4. Target: new volume seeded with the source RDB, then started
Invoke-Target create redis 2>&1 | Set-Content (Join-Path $outputDirectory '05-target-create.log')
$targetVolume = "${targetProject}_redis8-data"
$volumeExists = (docker volume ls -q --filter "name=^${targetVolume}$") -eq $targetVolume
Write-Check 'target.new-volume' $volumeExists $targetVolume
docker run --rm -v "${targetVolume}:/data" -v "$($outputDirectory):/in:ro" --entrypoint sh $targetImage -c 'test ! -e /data/dump.rdb && cp /in/source-redis.rdb /data/dump.rdb && chown redis:redis /data/dump.rdb' 2>&1 | Out-Null
Write-Check 'target.seeded-from-rdb-copy' ($LASTEXITCODE -eq 0) 'copied into an empty volume before first start'
Invoke-Target up -d --wait --wait-timeout 120 redis 2>&1 | Set-Content (Join-Path $outputDirectory '06-target-up.log')
Write-Check 'target.up-healthy' ($LASTEXITCODE -eq 0) ''
$targetInfo = Invoke-Redis 'target' 'redis' @('INFO server')
$targetVersion = "$(($targetInfo | Where-Object { $_ -like 'redis_version:*' }) -replace 'redis_version:', '')"
$targetModules = @(Invoke-Redis 'target' 'redis' @('MODULE LIST'))
$moduleList = (0..($targetModules.Count - 1) | Where-Object { $targetModules[$_] -eq 'name' } | ForEach-Object { "$($targetModules[$_ + 1])@$($targetModules[$_ + 3])" }) -join ','
Write-Check 'target.version-and-modules' ($targetVersion -eq '8.10.2' -and $moduleList -match 'search@' -and $moduleList -match 'timeseries@') "version=$targetVersion modules=[$moduleList]"
$loadLog = Invoke-Target logs --no-color redis 2>&1 | Out-String
Write-Check 'target.loaded-rdb-from-8.4.0' ($loadLog -match 'Loading RDB produced by version 8\.4\.0' -and $loadLog -match 'Done loading RDB') ''

# MIGRATE COPY from the module servers into the target (DUMP/RESTORE with TTL; the source keys stay in place).
$migrateHost = 'host.docker.internal'
foreach ($pair in @(@('redis-search', $sourceSearchKeys), @('redis-timeseries', $sourceSeriesKeys))) {
    $service = $pair[0]; $keys = @($pair[1] | ForEach-Object { $_.Key })
    $reply = @(Invoke-Redis 'source' $service @("MIGRATE $migrateHost $TargetPort '' 0 10000 COPY AUTH $redisPassword KEYS $($keys -join ' ')")) -join ' '
    Write-Check "transfer.migrate-copy-$service" ($reply -eq 'OK') ($reply -replace [regex]::Escape($redisPassword), '***')
}
# Derived metadata is not carried by DUMP/RESTORE: the Search index definition and the Time Series compaction rules
# are re-created on the target from the source definitions (observed: a restored series arrives without its rules).
Invoke-Redis 'target' 'redis' @('FT.CREATE idx:p15:product ON HASH PREFIX 1 p15:search:product: SCHEMA name TEXT SORTABLE price NUMERIC SORTABLE category TAG') | Out-Null
$rulesBefore = @(Invoke-Redis 'target' 'redis' @('TS.INFO p15:ts:sensor:raw') | Where-Object { $_ -eq 'p15:ts:sensor:avg' }).Count
Write-Check 'transfer.series-rules-not-carried-by-restore' ($rulesBefore -eq 0) "rules-after-restore=$rulesBefore"
$ruleReply = @(Invoke-Redis 'target' 'redis' @('TS.CREATERULE p15:ts:sensor:raw p15:ts:sensor:avg AGGREGATION avg 60000')) -join ' '
Write-Check 'transfer.series-rules-recreated' ($ruleReply -eq 'OK') $ruleReply
Start-Sleep -Seconds 2

function Test-TargetParity([string]$Stage) {
    $elapsed = ((Get-Date) - $capturedAt).TotalMilliseconds
    $cache = Get-State 'target' 'redis' 'template:product:v1:*'
    $search = Get-State 'target' 'redis' 'p15:search:*'
    $series = Get-State 'target' 'redis' 'p15:ts:*'
    Write-Check "$Stage.cache-keys-types-values-ttl" ((Compare-State $sourceCache $cache $elapsed) -eq '') (Compare-State $sourceCache $cache $elapsed)
    Write-Check "$Stage.search-keys-values-ttl" ((Compare-State $sourceSearchKeys $search $elapsed) -eq '') (Compare-State $sourceSearchKeys $search $elapsed)
    Write-Check "$Stage.series-keys-and-samples" ((Compare-State $sourceSeriesKeys $series $elapsed) -eq '') (Compare-State $sourceSeriesKeys $series $elapsed)
    $searchState = Get-SearchState 'target' 'redis'
    Write-Check "$Stage.search-results" ($searchState -ceq $sourceSearch) $searchState
    $seriesState = Get-SeriesState 'target' 'redis'
    Write-Check "$Stage.series-metadata-and-ranges" ($seriesState -ceq $sourceSeries) $seriesState
    $dbSize = @(Invoke-Redis 'target' 'redis' @('DBSIZE'))[0]
    Write-Check "$Stage.key-count" ($dbSize -eq '12') "dbsize=$dbSize"
    "$Stage cache=$($cache.Count) search=$($search.Count) series=$($series.Count) dbsize=$dbSize`nsearch: $searchState`nseries: $seriesState" | Add-Content (Join-Path $outputDirectory '07-target-parity.txt')
}
Test-TargetParity 'parity'

# ---------------------------------------------------------------- 5. Authentication and ACL on the target
$noAuth = @(Invoke-Redis 'target' 'redis' @('GET template:product:v1:any') -NoAuth) -join ' '
Write-Check 'acl.unauthenticated-rejected' ($noAuth -match 'NOAUTH') ''
$aclUsers = @(Invoke-Redis 'target' 'redis' @('ACL USERS')) -join ','
Write-Check 'acl.only-default-user' ($aclUsers -eq 'default') $aclUsers
$categories = @(Invoke-Redis 'target' 'redis' @('ACL CAT')) -join ','
Write-Check 'acl.module-categories' ($categories -match 'search' -and $categories -match 'timeseries' -and $categories -match 'json') ''
# Least-privilege dry runs (ACL DRYRUN executes nothing). The rehearsal user is in memory only (no ACL file) and
# disappears on restart.
$cacheRules = 'on >synthetic-dry-run-only ~template:product:* &__Booksleeve_MasterChanged +@connection +@read +@write +@keyspace +@hash +@transaction +@scripting +info +subscribe +unsubscribe +client -@dangerous -@admin -@search -@timeseries -@json'
Invoke-Redis 'target' 'redis' @("ACL SETUSER p15-dryrun-cache reset $cacheRules", "ACL SETUSER p15-dryrun-search reset on >synthetic-dry-run-only ~p15:* +@connection +@search +@timeseries +@read -@write -ft.dropindex -ft.create -ft.alter") | Out-Null
$dry = @{
    'cache.hget' = 'ACL DRYRUN p15-dryrun-cache HGETALL template:product:v1:x'; 'cache.hset' = 'ACL DRYRUN p15-dryrun-cache HSET template:product:v1:x data y'
    'cache.expire' = 'ACL DRYRUN p15-dryrun-cache EXPIRE template:product:v1:x 10'; 'cache.del' = 'ACL DRYRUN p15-dryrun-cache DEL template:product:v1:x'
    'cache.other-namespace' = 'ACL DRYRUN p15-dryrun-cache GET p15:search:product:1'; 'cache.flushall' = 'ACL DRYRUN p15-dryrun-cache FLUSHALL'
    'cache.config' = 'ACL DRYRUN p15-dryrun-cache CONFIG SET maxmemory 1'; 'cache.ft-search' = 'ACL DRYRUN p15-dryrun-cache FT.SEARCH idx:p15:product widget'
    'cache.ts-add' = 'ACL DRYRUN p15-dryrun-cache TS.ADD template:product:v1:x * 1'
    'search.ft-search' = 'ACL DRYRUN p15-dryrun-search FT.SEARCH idx:p15:product widget'; 'search.ts-range' = 'ACL DRYRUN p15-dryrun-search TS.RANGE p15:ts:sensor:raw - +'
    'search.ts-add' = 'ACL DRYRUN p15-dryrun-search TS.ADD p15:ts:sensor:raw * 1'; 'search.ft-dropindex' = 'ACL DRYRUN p15-dryrun-search FT.DROPINDEX idx:p15:product'
}
$allowed = @('cache.hget', 'cache.hset', 'cache.expire', 'cache.del', 'search.ft-search', 'search.ts-range')
foreach ($name in ($dry.Keys | Sort-Object)) {
    $reply = @(Invoke-Redis 'target' 'redis' @($dry[$name])) -join ' '
    $ok = if ($name -in $allowed) { $reply -eq 'OK' } else { $reply -match 'not allowed|NOPERM|no permissions' }
    Write-Check "acl.dryrun.$name" $ok $reply
}

# ---------------------------------------------------------------- 6. Restart persistence of the target
Invoke-Target stop redis 2>&1 | Out-Null
$stopLog = Invoke-Target logs --no-color --tail 20 redis 2>&1 | Out-String
Write-Check 'restart.snapshot-on-shutdown' ($stopLog -match 'DB saved on disk') ''
Invoke-Target up -d --no-recreate --wait --wait-timeout 120 redis 2>&1 | Out-Null
Write-Check 'restart.target-healthy-again' ($LASTEXITCODE -eq 0) ''
Test-TargetParity 'restart'
$aclAfterRestart = @(Invoke-Redis 'target' 'redis' @('ACL USERS')) -join ','
Write-Check 'restart.in-memory-dryrun-users-gone' ($aclAfterRestart -eq 'default') $aclAfterRestart
# The re-created rule compacts new samples on the migrated series (a rule does not backfill older samples).
Invoke-Redis 'target' 'redis' @("TS.ADD p15:ts:sensor:raw $($tsStart + 180000) 100", "TS.ADD p15:ts:sensor:raw $($tsStart + 181000) 200", "TS.ADD p15:ts:sensor:raw $($tsStart + 240000) 1") | Out-Null
$avgAfter = @(Invoke-Redis 'target' 'redis' @('TS.RANGE p15:ts:sensor:avg - +')) -join ','
Write-Check 'restart.series-rule-active-after-migration' ($avgAfter -match "$($tsStart + 180000),150") $avgAfter
$targetMounts = docker inspect (Invoke-Target ps -q redis | Out-String).Trim() --format '{{range .Mounts}}{{.Name}} {{end}}'
Write-Check 'isolation.target-mounts-only-target-volume' ($targetMounts.Trim() -eq $targetVolume) $targetMounts.Trim()

# ---------------------------------------------------------------- 7. Rollback: target off, untouched source back on
Invoke-Target stop 2>&1 | Out-Null
Invoke-Source stop 2>&1 | Out-Null
Invoke-Source up -d --no-recreate --wait --wait-timeout 180 @sourceServices 2>&1 | Set-Content (Join-Path $outputDirectory '08-rollback-up.log')
Write-Check 'rollback.source-starts' ($LASTEXITCODE -eq 0) ''
$sourceMounts = docker inspect (Invoke-Source ps -q redis | Out-String).Trim() --format '{{range .Mounts}}{{.Name}} {{end}}'
Write-Check 'rollback.source-uses-its-own-volume' ($sourceMounts.Trim() -eq "${sourceProject}_redis-volume-data") $sourceMounts.Trim()
$elapsed = ((Get-Date) - $capturedAt).TotalMilliseconds
$rollbackCache = Get-State 'source' 'redis' 'template:product:v1:*'
Write-Check 'rollback.source-cache-unchanged' ((Compare-State $sourceCache $rollbackCache $elapsed) -eq '') (Compare-State $sourceCache $rollbackCache $elapsed)
$sourceVersion = "$(((Invoke-Redis 'source' 'redis' @('INFO server')) | Where-Object { $_ -like 'redis_version:*' }) -replace 'redis_version:', '')"
Write-Check 'rollback.source-version' ($sourceVersion -eq '8.4.0') "version=$sourceVersion"
# The module servers have no named volume (as in the pre-Phase 15 topology): their data survives stop/start of the
# same container, which is what a rollback does.
Write-Check 'rollback.source-search-unchanged' ((Get-SearchState 'source' 'redis-search') -ceq $sourceSearch) ''
Write-Check 'rollback.source-series-unchanged' ((Get-SeriesState 'source' 'redis-timeseries') -ceq $sourceSeries) ''

# ---------------------------------------------------------------- 8. Stop without deleting anything
Invoke-Source stop 2>&1 | Out-Null
Invoke-Target stop 2>&1 | Out-Null
$volumes = (docker volume ls --format '{{.Name}}' | Where-Object { $_ -like "$sourceProject*" -or $_ -like "$targetProject*" } | Sort-Object) -join ','
Write-Check 'retained.project-volumes' ($volumes -match 'redis8-data' -and $volumes -match 'redis-volume-data') $volumes
Write-Check 'isolation.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''

Write-Check 'redis-cli.no-unexpected-errors' ($script:unexpectedRedisErrors.Count -eq 0) (($script:unexpectedRedisErrors | Select-Object -First 5) -join ' | ')
$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$failed = @($results | Where-Object { -not $_.Passed })
"source=$sourceProject target=$targetProject checks=$($results.Count) failed=$($failed.Count)" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
