<#
.SYNOPSIS
    Phase 16 isolated PostgreSQL 16 checkpoint: both-database backup/restore, 16.11 -> 16.15 patch, application
    validation, restart persistence, and a timed switch back to the preserved 16.11 baseline.
.DESCRIPTION
    Every resource is new and named after the RunId; nothing is published except the WebApi on a loopback port:
      network    p16-net-<RunId>
      source     p16-src-<RunId>  (16.11, the Phase 14 pin; volume p16-src-<RunId>-data, init-db.sh; rollback baseline)
      restore    p16-rst-<RunId>  (16.15; volume p16-rst-<RunId>-data; pg_dumpall globals + pg_restore --create)
      patch      p16-pat-<RunId>  (16.15 on p16-pat-<RunId>-data, a file copy of the stopped source volume: the
                                   in-place minor-update path; the source volume itself is only ever opened by 16.11)
      webapi     p16-api-{src,rst,pat,rb}-<RunId> from image p16-webapi:<RunId> (cache, broker, search, Elastic logging
                                   and Elasticsearch health check disabled, so readiness depends on both databases only)
    Data is synthetic: the WebApi's own migrations and seeds plus a few synthetic API writes. Credentials are random,
    passed to Docker through the process environment only, and never printed. The script never removes containers,
    volumes, images, networks, databases or backups; it ends with `docker stop`. Evidence (inventories, fingerprints
    as row counts + md5 digests, plans, timings, backups with SHA-256) goes to artifacts/validation/pg16-rehearsal/<RunId>.
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$WebApiPort = 18160,
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$baselineImage = 'postgres:16.11@sha256:056b54f00419b49289227ab12d09df508543883f407fe9935a2cec430ef8aa8d'
$targetImage = 'postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d'
$network = "p16-net-$RunId"
$source = "p16-src-$RunId"; $restore = "p16-rst-$RunId"; $patch = "p16-pat-$RunId"
$webApiImage = "p16-webapi:$RunId"
$appDb = 'TemplateCleanArch'; $identityDb = 'TemplateCleanArchIdentity'
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\pg16-rehearsal\$RunId"
$backupDirectory = Join-Path $outputDirectory 'backup'
New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
$baseUri = "http://127.0.0.1:$WebApiPort"
$results = New-Object System.Collections.Generic.List[object]
$timings = [ordered]@{}

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function Save([string]$Name, $Content) { $Content | Set-Content -Path (Join-Path $outputDirectory $Name) -Encoding utf8 }

function New-Secret { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

# Synthetic credentials live only in this process environment; `docker run -e NAME` copies them without printing.
$env:POSTGRES_USER = "p16_$((New-Secret).Substring(0, 8))"
$env:POSTGRES_PASSWORD = New-Secret
$env:PGPASSWORD = $env:POSTGRES_PASSWORD
$env:POSTGRES_DB_NAME = $appDb
$env:POSTGRES_IDENTITY_DB_NAME = $identityDb
# Phase 22: per-run JWT signing key from a cryptographically secure source; `docker run -e NAME` copies it unprinted.
$jwtKeyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($jwtKeyBytes)
$env:JwtSettings__Key = [Convert]::ToBase64String($jwtKeyBytes)
$user = $env:POSTGRES_USER

function Invoke-Psql([string]$Container, [string]$Database, [string]$Sql) {
    $output = $Sql | docker exec -i $Container psql -U $user -d $Database -tA -F '|' -v ON_ERROR_STOP=1 -v "seed_user=$script:seedUserNormalized" 2>&1
    return (($output | Out-String) -replace [regex]::Escape($user), '<user>').Trim()
}

function Start-Postgres([string]$Name, [string]$Image, [bool]$WithInit) {
    $arguments = @('run', '-d', '--name', $Name, '--network', $network, '--network-alias', $Name,
        '-e', 'POSTGRES_USER', '-e', 'POSTGRES_PASSWORD', '-e', 'POSTGRES_DB_NAME', '-e', 'POSTGRES_IDENTITY_DB_NAME',
        '-v', "$Name-data:/var/lib/postgresql/data")
    if ($WithInit) { $arguments += @('-v', "$(Join-Path $repositoryRoot 'init-db.sh'):/docker-entrypoint-initdb.d/init-db.sh:ro") }
    $arguments += $Image
    & docker @arguments | Out-Null
}

function Wait-Postgres([string]$Name, [string[]]$Databases) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        # TCP probe: the temporary init server listens on the Unix socket only, so this cannot pass early.
        $ok = $true
        foreach ($database in $Databases) {
            docker exec $Name psql -h 127.0.0.1 -U $user -d $database -tAc 'SELECT 1' 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) { $ok = $false; break }
        }
        if ($ok) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Start-WebApi([string]$Name, [string]$DatabaseHost) {
    $env:ConnectionStrings__DefaultConnection = "Host=$DatabaseHost;Port=5432;Username=$user;Password=$($env:POSTGRES_PASSWORD);Database=$appDb;SearchPath=public"
    $env:ConnectionStrings__IdentityConnection = "Host=$DatabaseHost;Port=5432;Username=$user;Password=$($env:POSTGRES_PASSWORD);Database=$identityDb;SearchPath=public"
    $arguments = @('run', '-d', '--name', $Name, '--network', $network, '-p', "127.0.0.1:${WebApiPort}:8080",
        '-e', 'ConnectionStrings__DefaultConnection', '-e', 'ConnectionStrings__IdentityConnection', '-e', 'JwtSettings__Key',
        '-e', 'ASPNETCORE_ENVIRONMENT=Production', '-e', 'ASPNETCORE_HTTP_PORTS=8080',
        '-e', 'CacheSettings__Enabled=false', '-e', 'RabbitMqSettings__Enabled=false', '-e', 'PersonSearchSettings__Enabled=false',
        '-e', 'ElasticLoggingSettings__Enabled=false', '-e', 'ElasticsearchHealthCheckSettings__Enabled=false', $webApiImage)
    & docker @arguments | Out-Null
}

function Wait-HttpOk([string]$Path, [int]$Seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-WebRequest -Uri "$baseUri$Path" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { return $true } } catch { }
        Start-Sleep -Milliseconds 500
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

# Row count + md5 of every user table (no payload leaves the database), plus the sequences' last values.
$fingerprintSql = @'
SELECT format('SELECT %L, count(*), md5(coalesce(string_agg(t::text, %L ORDER BY t::text), %L)) FROM %I.%I t',
              n.nspname || '.' || c.relname, '|', '', n.nspname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
ORDER BY 1 \gexec
SELECT 'sequence', schemaname || '.' || sequencename, coalesce(last_value::text, 'null') FROM pg_sequences ORDER BY 2;
'@

# Server-independent structure: compared verbatim between the source and every restored/patched copy.
$structureSql = @'
SELECT 'encoding', pg_encoding_to_char(encoding), datcollate, datctype, datlocprovider, coalesce(datcollversion, 'null'),
       coalesce(pg_database_collation_actual_version(oid), 'null'), pg_get_userbyid(datdba) = current_user
FROM pg_database WHERE datname = current_database();
SELECT 'setting', name, setting FROM pg_settings WHERE name IN ('server_encoding', 'lc_collate', 'lc_ctype', 'TimeZone', 'DateStyle', 'standard_conforming_strings') ORDER BY name;
SELECT 'extension', extname, extversion FROM pg_extension ORDER BY extname;
SELECT 'schema', nspname, pg_get_userbyid(nspowner) = current_user, coalesce(array_to_string(nspacl, ','), '') FROM pg_namespace
WHERE nspname NOT IN ('pg_catalog', 'information_schema') AND nspname NOT LIKE 'pg_t%' ORDER BY nspname;
SELECT 'relation', n.nspname || '.' || c.relname, c.relkind, pg_get_userbyid(c.relowner) = current_user, coalesce(array_to_string(c.relacl, ','), '')
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_t%' ORDER BY 2;
SELECT 'column', table_schema || '.' || table_name || '.' || column_name, data_type, coalesce(numeric_precision::text, ''), coalesce(numeric_scale::text, ''),
       coalesce(character_maximum_length::text, ''), is_nullable, coalesce(column_default, ''), coalesce(collation_name, '')
FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY 2;
SELECT 'index', schemaname || '.' || indexname, indexdef FROM pg_indexes WHERE schemaname NOT IN ('pg_catalog', 'information_schema') ORDER BY 2;
SELECT 'constraint', conrelid::regclass::text || '.' || conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace NOT IN ('pg_catalog'::regnamespace, 'information_schema'::regnamespace) ORDER BY 2;
SELECT 'grant', table_schema || '.' || table_name, privilege_type, grantee = current_user FROM information_schema.role_table_grants
WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY 2, 3, 4;
SELECT 'default_acl', count(*) FROM pg_default_acl;
SELECT 'roles', count(*), bool_and(rolsuper), bool_or(rolname = current_user) FROM pg_roles WHERE rolname !~ '^pg_';
SELECT 'triggers_user', count(*) FROM pg_trigger WHERE NOT tgisinternal;
SELECT 'functions_user', count(*) FROM pg_proc WHERE pronamespace NOT IN ('pg_catalog'::regnamespace, 'information_schema'::regnamespace);
SELECT 'types_user', count(*) FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_t%' AND t.typtype IN ('e', 'd', 'c', 'r') AND t.typrelid = 0;
SELECT 'generated_columns', count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE a.attgenerated <> '' AND n.nspname NOT IN ('pg_catalog', 'information_schema');
SELECT 'large_objects', count(*) FROM pg_largeobject_metadata;
SELECT 'collations_used', coalesce(string_agg(DISTINCT coalesce(collation_name, 'default'), ','), '') FROM information_schema.columns
WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND data_type IN ('text', 'character varying', 'character');
'@

$migrationSql = @'
SELECT 'migration', "MigrationId", "ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY 2;
'@

# Read-only representative queries derived from ProductRepository, GenericRepository and the Identity seed/login path.
# Results leave the database only as a row count and an md5 digest.
$appQueries = [ordered]@{
    'product-by-id'       = 'SELECT "Id", "Name", "Price", "BarCode" FROM "Products" WHERE "Id" = ''c9770569-499d-4b25-59af-08dd5e76cb62'''
    'product-paged'       = 'SELECT "Id", "Name", "Price" FROM "Products" ORDER BY "Created", "Id" LIMIT 5 OFFSET 0'
    'product-filtered'    = 'SELECT "Id" FROM "Products" WHERE strpos("Name", ''o'') > 0 ORDER BY "Created", "Id" LIMIT 10'
    'product-price-scale' = 'SELECT "Id", "Price"::text, scale("Price") FROM "Products" ORDER BY "Id"'
    'collation-ordering'  = 'SELECT x FROM (VALUES (''a''), (''B''), (''Ã¤''), (''_z''), (''Z1''), (''Ã©''), (''E'')) v(x) ORDER BY x'
}
$identityQueries = [ordered]@{
    'user-by-normalized-name' = 'SELECT "Id", "SecurityStamp" IS NOT NULL, "PasswordHash" IS NOT NULL FROM "Identity"."User" WHERE "NormalizedUserName" = :''seed_user'''
    'user-roles'              = 'SELECT r."NormalizedName" FROM "Identity"."User" u JOIN "Identity"."UserRoles" ur ON ur."UserId" = u."Id" JOIN "Identity"."Role" r ON r."Id" = ur."RoleId" WHERE u."NormalizedUserName" = :''seed_user'' ORDER BY 1'
    'roles-ordered'           = 'SELECT "NormalizedName" FROM "Identity"."Role" ORDER BY "NormalizedName"'
}

function Get-QueryEvidence([string]$Container) {
    $digests = New-Object System.Collections.Generic.List[string]
    $plans = New-Object System.Collections.Generic.List[string]
    foreach ($set in @(@{ Db = $appDb; Queries = $appQueries }, @{ Db = $identityDb; Queries = $identityQueries })) {
        foreach ($name in $set.Queries.Keys) {
            $query = $set.Queries[$name]
            $digests.Add("$name|" + (Invoke-Psql $Container $set.Db "SELECT count(*), md5(coalesce(string_agg(q::text, '|'), '')) FROM ($query) q;"))
            $shape = Invoke-Psql $Container $set.Db "EXPLAIN (COSTS OFF) $query;"
            # Index usability independent of tiny-table costing: the same plan with sequential scans discouraged.
            $shape += "
-- index
" + (Invoke-Psql $Container $set.Db "SET enable_seqscan = off; EXPLAIN (COSTS OFF) $query;")
            $analyze = Invoke-Psql $Container $set.Db "EXPLAIN (ANALYZE, BUFFERS) $query;"
            $plans.Add("### $name`n-- shape`n$shape`n-- analyze`n$analyze`n")
        }
    }
    return [pscustomobject]@{ Digests = ($digests -join "`n"); Plans = ($plans -join "`n") }
}

function Get-PlanShapes([string]$PlanText) {
    # Only the "-- shape" blocks: node types and index usage, without timings.
    return ([regex]::Matches($PlanText, '(?s)-- shape\n(.*?)\n-- analyze') | ForEach-Object { $_.Groups[1].Value }) -join "`n"
}

function Get-Snapshot([string]$Container) {
    $parts = foreach ($database in @($appDb, $identityDb)) {
        "[$database]"; Invoke-Psql $Container $database $structureSql; Invoke-Psql $Container $database $migrationSql; Invoke-Psql $Container $database $fingerprintSql
    }
    return ($parts -join "`n")
}

function Get-Fingerprint([string]$Container) {
    return (@($appDb, $identityDb) | ForEach-Object { "[$_]"; Invoke-Psql $Container $_ $fingerprintSql; Invoke-Psql $Container $_ $migrationSql }) -join "`n"
}

function Get-ServerInfo([string]$Container) {
    return Invoke-Psql $Container 'postgres' "SELECT 'server', version(); SELECT 'server_version_num', current_setting('server_version_num'); SELECT 'database', datname, pg_size_pretty(pg_database_size(oid)), pg_database_size(oid) FROM pg_database WHERE datname IN ('$appDb', '$identityDb') ORDER BY 2;"
}

function Get-NormalVolumes {
    $names = docker volume ls --format '{{.Name}}' | Where-Object { $_ -like 'cleanarchitecture_*' -or $_ -like 'templatecleanarchnet_*' -or $_ -like '*postgres*' -and $_ -notlike "p16-*-$RunId-data" }
    return ($names | ForEach-Object { "$_@" + (docker volume inspect $_ --format '{{.CreatedAt}}') } | Sort-Object) -join ';'
}

# ---------------------------------------------------------------- 0. Isolation, images, capacity
$taken = @(docker ps -a --format '{{.Names}}' | Where-Object { $_ -like "p16-*-$RunId" }) + @(docker volume ls --format '{{.Name}}' | Where-Object { $_ -like "p16-*-$RunId-data" })
if ($taken.Count -gt 0 -or (docker network ls --format '{{.Name}}' | Where-Object { $_ -eq $network })) { throw "Resources for RunId $RunId already exist; choose another RunId." }
$normalVolumesBefore = Get-NormalVolumes
Save 'resources.txt' @("network=$network", "source=$source volume=$source-data image=$baselineImage", "restore=$restore volume=$restore-data image=$targetImage",
    "patch=$patch volume=$patch-data image=$targetImage", "webapi-image=$webApiImage", "webapi-containers=p16-api-{src,rst,pat,rb}-$RunId", "backup=$backupDirectory")
Write-Check 'isolation.new-resources' $true "run=$RunId"

$freeGb = [math]::Round((Get-PSDrive ((Resolve-Path $outputDirectory).Drive.Name)).Free / 1GB, 1)
Write-Check 'capacity.free-disk' ($freeGb -gt 5) "freeGB=$freeGb"

foreach ($image in $baselineImage, $targetImage) {
    docker pull -q $image 2>&1 | Out-Null
    $version = (docker run --rm --entrypoint postgres $image --version 2>&1 | Out-String).Trim()
    $libc = (docker run --rm --entrypoint sh $image -c 'ldd --version | head -1; cat /etc/debian_version; uname -m' 2>&1 | Out-String).Trim() -replace "`r?`n", ' / '
    Save "image-$((($image -split '@')[0]) -replace '[:/]', '-').txt" "$image`n$version`n$libc"
    $expected = (($image -split '@')[0] -split ':')[1]
    Write-Check "image.version.$expected" ($version -match "PostgreSQL\) $([regex]::Escape($expected)) ") "$version | $libc"
}

$started = Get-Date
docker build -q -f Src/Presentation/Template.WebApi/Dockerfile -t $webApiImage . 2>&1 | Set-Content (Join-Path $outputDirectory '00-webapi-build.log')
Write-Check 'webapi.image-built' ($LASTEXITCODE -eq 0) "seconds=$([int]((Get-Date) - $started).TotalSeconds)"
docker network create $network 2>&1 | Out-Null

$seedSource = Get-Content 'Src\Infrastructure\Template.Infrastructure.Identity\Seeds\DefaultBasicUser.cs' -Raw
$seedUser = [regex]::Match($seedSource, 'UserName\s*=\s*"([^"]+)"').Groups[1].Value
$seedPassword = [regex]::Match($seedSource, 'CreateAsync\(defaultUser,\s*"([^"]+)"\)').Groups[1].Value
$script:seedUserNormalized = $seedUser.ToUpperInvariant()

# ---------------------------------------------------------------- 1. Source 16.11 (rollback baseline) with synthetic data
Start-Postgres $source $baselineImage $true
Write-Check 'source.ready-both-databases' (Wait-Postgres $source @($appDb, $identityDb)) $baselineImage
Start-WebApi "p16-api-src-$RunId" $source
Write-Check 'source.webapi-migrated-and-seeded' (Wait-HttpOk '/health/ready') '/health/ready'

$auth = Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = $seedPassword } | ConvertTo-Json -Compress)
$token = if ($auth.Status -eq 200) { ($auth.Content | ConvertFrom-Json).data.jwToken } else { $null }
Write-Check 'source.identity-authenticate' ([bool]$token) "status=$($auth.Status)"
$bearer = @{ Authorization = "Bearer $token" }
$keep = Get-Status 'POST' '/api/v2/product' (@{ name = 'P16 synthetic kept'; price = 1234.56; barCode = 'P16-KEEP' } | ConvertTo-Json -Compress) $bearer
$keepId = if ($keep.Status -eq 201) { ($keep.Content | ConvertFrom-Json).data.id } else { $null }
$update = Get-Status 'PUT' "/api/v2/product/$keepId" (@{ name = 'P16 synthetic kept v2'; price = 9876543.21; barCode = 'P16-KEEP' } | ConvertTo-Json -Compress) $bearer
$drop = Get-Status 'POST' '/api/v2/product' (@{ name = 'P16 synthetic deleted'; price = 0.01; barCode = 'P16-DEL' } | ConvertTo-Json -Compress) $bearer
$dropId = if ($drop.Status -eq 201) { ($drop.Content | ConvertFrom-Json).data.id } else { $null }
$delete = Get-Status 'DELETE' "/api/v2/product/$dropId" '' $bearer
Write-Check 'source.synthetic-writes' ($keep.Status -eq 201 -and $update.Status -eq 200 -and $delete.Status -eq 204) "create=$($keep.Status) update=$($update.Status) delete=$($delete.Status)"
# Write freeze for a consistent two-database recovery point: the only writer stops before the backup.
docker stop -t 30 "p16-api-src-$RunId" 2>&1 | Out-Null
$recoveryPoint = (Get-Date).ToUniversalTime().ToString('o')

# Same statistics on both sides: plans are compared after ANALYZE (catalog statistics only, no user data change).
foreach ($database in $appDb, $identityDb) { Invoke-Psql $source $database 'ANALYZE;' | Out-Null }
$sourceServer = Get-ServerInfo $source
$sourceSnapshot = Get-Snapshot $source
$sourceQueries = Get-QueryEvidence $source
Save '01-source-server.txt' $sourceServer
Save '01-source-snapshot.txt' $sourceSnapshot
Save '01-source-query-digests.txt' $sourceQueries.Digests
Save '01-source-plans.txt' $sourceQueries.Plans
Write-Check 'source.migration-history' ([regex]::Matches($sourceSnapshot, '(?m)^migration\|').Count -ge 2) "rows=$([regex]::Matches($sourceSnapshot, '(?m)^migration\|').Count)"
Write-Check 'source.no-collation-version-mismatch' (($sourceSnapshot -split "`n" | Where-Object { $_ -like 'encoding|*' } | Where-Object { ($_ -split '\|')[5] -ne ($_ -split '\|')[6] }).Count -eq 0) ''

# ---------------------------------------------------------------- 2. Backup (16.15 client tools over the private network)
$backupStarted = Get-Date
function Invoke-Tool([string[]]$ToolArguments) {
    $arguments = @('run', '--rm', '--network', $network, '-e', 'PGPASSWORD', '-v', "${backupDirectory}:/backup", '--entrypoint', $ToolArguments[0], $targetImage) + $ToolArguments[1..($ToolArguments.Count - 1)]
    $output = & docker @arguments 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -replace [regex]::Escape($user), '<user>').Trim() }
}
$globals = Invoke-Tool @('pg_dumpall', '-h', $source, '-U', $user, '--globals-only', '--no-role-passwords', '-f', '/backup/globals.sql')
$appDump = Invoke-Tool @('pg_dump', '-h', $source, '-U', $user, '-d', $appDb, '-Fc', '-f', '/backup/application.dump')
$identityDump = Invoke-Tool @('pg_dump', '-h', $source, '-U', $user, '-d', $identityDb, '-Fc', '-f', '/backup/identity.dump')
$timings['backup-seconds'] = [math]::Round(((Get-Date) - $backupStarted).TotalSeconds, 1)
Write-Check 'backup.globals' ($globals.Code -eq 0 -and -not $globals.Output) $globals.Output
Write-Check 'backup.application' ($appDump.Code -eq 0 -and -not $appDump.Output) $appDump.Output
Write-Check 'backup.identity' ($identityDump.Code -eq 0 -and -not $identityDump.Output) $identityDump.Output
$manifest = Get-ChildItem $backupDirectory -File | ForEach-Object { "$($_.Name)|$($_.Length)|$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)" }
Save '02-backup-manifest.txt' (@("recovery_point_utc=$recoveryPoint", "tool=$targetImage", "server=$baselineImage") + $manifest)
foreach ($dump in 'application', 'identity') {
    $list = Invoke-Tool @('pg_restore', '--list', "/backup/$dump.dump")
    Save "02-backup-$dump-toc.txt" $list.Output
    Write-Check "backup.$dump-toc-readable" ($list.Code -eq 0 -and $list.Output -match 'TABLE DATA') "entries=$(([regex]::Matches($list.Output, '(?m)^\d+;')).Count)"
}
$globalsText = (Get-Content (Join-Path $backupDirectory 'globals.sql') -Raw)
Write-Check 'backup.globals-no-password' ($globalsText -notmatch '(?i)PASSWORD\s+''|SCRAM-SHA-256\$|md5[0-9a-f]{32}') ''
$globalRoles = @([regex]::Matches($globalsText, '(?m)^CREATE ROLE (\S+);') | ForEach-Object { $_.Groups[1].Value })
Write-Check 'backup.globals-only-bootstrap-role' ($globalRoles.Count -eq 1 -and $globalRoles[0] -eq $user) "roles=$($globalRoles.Count)"

# ---------------------------------------------------------------- 3. Restore into a new 16.15 server
Start-Postgres $restore $targetImage $false
Write-Check 'restore.target-ready' (Wait-Postgres $restore @('postgres')) $targetImage
$restoreStarted = Get-Date
# The bootstrap role already exists on the new server; it is the only global object (checked above), so the globals
# file is applied without ON_ERROR_STOP and the only accepted error is that role's "already exists".
$globalsApply = (Get-Content (Join-Path $backupDirectory 'globals.sql') -Raw) | docker exec -i $restore psql -U $user -d postgres 2>&1 | Out-String
$globalErrors = @($globalsApply -split "`n" | Where-Object { $_ -match 'ERROR:' -and $_ -notmatch 'role .* already exists' })
Write-Check 'restore.globals' ($globalErrors.Count -eq 0) ($globalErrors -join ' ')
$appRestore = Invoke-Tool @('pg_restore', '-h', $restore, '-U', $user, '-d', 'postgres', '--create', '--exit-on-error', '/backup/application.dump')
$identityRestore = Invoke-Tool @('pg_restore', '-h', $restore, '-U', $user, '-d', 'postgres', '--create', '--exit-on-error', '/backup/identity.dump')
$timings['restore-seconds'] = [math]::Round(((Get-Date) - $restoreStarted).TotalSeconds, 1)
Write-Check 'restore.application-no-errors' ($appRestore.Code -eq 0 -and -not $appRestore.Output) $appRestore.Output
Write-Check 'restore.identity-no-errors' ($identityRestore.Code -eq 0 -and -not $identityRestore.Output) $identityRestore.Output
Invoke-Psql $restore 'postgres' 'ANALYZE;' | Out-Null
foreach ($database in $appDb, $identityDb) { Invoke-Psql $restore $database 'ANALYZE;' | Out-Null }
Invoke-Psql $source 'postgres' 'SELECT 1;' | Out-Null

$restoreServer = Get-ServerInfo $restore
$restoreSnapshot = Get-Snapshot $restore
$restoreQueries = Get-QueryEvidence $restore
Save '03-restore-server.txt' $restoreServer
Save '03-restore-snapshot.txt' $restoreSnapshot
Save '03-restore-query-digests.txt' $restoreQueries.Digests
Save '03-restore-plans.txt' $restoreQueries.Plans
Write-Check 'restore.runtime-16.15' ($restoreServer -match 'PostgreSQL 16\.15 ') ''
Write-Check 'restore.structure-and-data-identical' ($restoreSnapshot -ceq $sourceSnapshot) 'encoding/locale/collation version, extensions, schemas, owners, grants, columns, indexes, constraints, migrations, sequences, per-table count+md5'
Write-Check 'queries.results-identical-16.11-vs-16.15' ($restoreQueries.Digests -ceq $sourceQueries.Digests) ''
$shapeSource = Get-PlanShapes $sourceQueries.Plans; $shapeRestore = Get-PlanShapes $restoreQueries.Plans
Save '03-plan-shape-diff.txt' (Compare-Object ($shapeSource -split "`n") ($shapeRestore -split "`n") | Out-String)
Write-Check 'queries.plan-shapes-identical' ($shapeSource -ceq $shapeRestore) 'EXPLAIN (COSTS OFF); see 03-plan-shape-diff.txt'

# Repeatability: the same documented restore into new databases on the same target gives the same fingerprint.
$repeatOk = $true
foreach ($pair in @(@($appDb, 'application'), @($identityDb, 'identity'))) {
    $copyName = "$($pair[0])_restore_check"
    Invoke-Psql $restore 'postgres' "CREATE DATABASE `"$copyName`" TEMPLATE template0;" | Out-Null
    $again = Invoke-Tool @('pg_restore', '-h', $restore, '-U', $user, '-d', $copyName, '--exit-on-error', "/backup/$($pair[1]).dump")
    $a = Invoke-Psql $restore $pair[0] $fingerprintSql; $b = Invoke-Psql $restore $copyName $fingerprintSql
    if ($again.Code -ne 0 -or $a -cne $b) { $repeatOk = $false }
}
Write-Check 'restore.repeatable' $repeatOk 'second restore into *_restore_check databases (retained)'

# ---------------------------------------------------------------- 4. Application on the restored 16.15 server
$apiStart = (Get-Date).ToUniversalTime().ToString('o')
Start-WebApi "p16-api-rst-$RunId" $restore
Write-Check 'app.ready-on-16.15' (Wait-HttpOk '/health/ready') ''
$logs = docker logs "p16-api-rst-$RunId" 2>&1 | Out-String
Write-Check 'app.no-migration-applied-on-restore' ($logs -notmatch 'Applying migration') ''
Write-Check 'app.seeds-idempotent-on-restore' ((Get-Fingerprint $restore) -ceq (Get-Fingerprint $source)) 'fingerprint unchanged after startup'
Write-Check 'app.no-secrets-in-logs' ($logs -notmatch [regex]::Escape($env:POSTGRES_PASSWORD) -and $logs -notmatch 'Password=') ''
foreach ($path in '/health', '/health/live', '/health/ready') { Write-Check "app.$path" ((Get-Status 'GET' $path).Status -eq 200) '' }
$health = (Get-Status 'GET' '/health/ready').Content
Write-Check 'app.health-sanitized' ($health -notmatch 'Host=|Password|Username') ''

$auth = Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = $seedPassword } | ConvertTo-Json -Compress)
$token = if ($auth.Status -eq 200) { ($auth.Content | ConvertFrom-Json).data.jwToken } else { $null }
$bearer = @{ Authorization = "Bearer $token" }
Write-Check 'app.identity-authenticate-on-restore' ([bool]$token) "status=$($auth.Status)"
Write-Check 'app.identity-wrong-password' ((Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = 'synthetic-wrong' } | ConvertTo-Json -Compress)).Status -eq 400) ''
Write-Check 'app.unauthenticated-post-401' ((Get-Status 'POST' '/api/v2/product' '{}').Status -eq 401) ''
$kept = Get-Status 'GET' "/api/v2/product/$keepId"
$keptPrice = if ($kept.Status -eq 200) { ($kept.Content | ConvertFrom-Json).data.price } else { $null }
Write-Check 'app.restored-product-decimal' ($kept.Content -match '"price":9876543\.21[,}]') "status=$($kept.Status) price=$keptPrice"
Write-Check 'app.seed-product-v1' ((Get-Status 'GET' '/api/v1/product/get-by-id?id=c9770569-499d-4b25-59af-08dd5e76cb62').Status -eq 200) ''
$paged = Get-Status 'GET' '/api/v2/product/get-paged-list?PageNumber=1&PageSize=3'
Write-Check 'app.paged-list' ($paged.Status -eq 200) ''
$new = Get-Status 'POST' '/api/v2/product' (@{ name = 'P16 restore write'; price = 0.99; barCode = 'P16-RST' } | ConvertTo-Json -Compress) $bearer
$newId = if ($new.Status -eq 201) { ($new.Content | ConvertFrom-Json).data.id } else { $null }
$newUpdate = Get-Status 'PUT' "/api/v2/product/$newId" (@{ name = 'P16 restore write v2'; price = 12345678901234.99; barCode = 'P16-RST' } | ConvertTo-Json -Compress) $bearer
$newGet = Get-Status 'GET' "/api/v2/product/$newId"
$newPrice = if ($newGet.Status -eq 200) { ($newGet.Content | ConvertFrom-Json).data.price } else { $null }
Write-Check 'app.crud-on-16.15' ($new.Status -eq 201 -and $newUpdate.Status -eq 200 -and $newGet.Content -match '"price":12345678901234\.99[,}]') "create=$($new.Status) update=$($newUpdate.Status) price=$newPrice"

docker restart -t 30 "p16-api-rst-$RunId" 2>&1 | Out-Null
$beforeSecond = Get-Fingerprint $restore
Write-Check 'app.second-startup-ready' (Wait-HttpOk '/health/ready') ''
$secondLogs = docker logs --since $apiStart "p16-api-rst-$RunId" 2>&1 | Out-String
Write-Check 'app.second-startup-idempotent' ((Get-Fingerprint $restore) -ceq $beforeSecond -and $secondLogs -notmatch 'Applying migration') ''

# Database restart: data persists and the application recovers without its own restart.
$beforeDbRestart = Get-Fingerprint $restore
docker restart -t 60 $restore 2>&1 | Out-Null
$downStatus = (Get-Status 'GET' '/health/ready').Status
Write-Check 'restart.postgres-back' (Wait-Postgres $restore @($appDb, $identityDb)) ''
Write-Check 'restart.app-recovers' (Wait-HttpOk '/health/ready' 120) "status-during-restart=$downStatus"
Write-Check 'restart.data-persisted' ((Get-Fingerprint $restore) -ceq $beforeDbRestart) ''
$dbLog = docker logs $restore 2>&1 | Out-String
Save '04-restore-postgres.log' (($dbLog -replace [regex]::Escape($user), '<user>') -split "`n" | Select-Object -Last 80)
Write-Check 'restart.clean-shutdown-recovery' ($dbLog -match 'database system was shut down at' -and $dbLog -notmatch 'PANIC|was interrupted') ''
Write-Check 'restart.product-after-restart' ((Get-Status 'GET' "/api/v2/product/$newId").Status -eq 200) ''
docker stop -t 30 "p16-api-rst-$RunId" 2>&1 | Out-Null
$shutdownLogs = docker logs "p16-api-rst-$RunId" 2>&1 | Out-String
Write-Check 'shutdown.webapi-graceful' ($shutdownLogs -match 'Application is shutting down|Application stopping') ''

# ---------------------------------------------------------------- 5. In-place minor update rehearsal on a file copy
docker stop -t 60 $source 2>&1 | Out-Null
docker run --rm -v "$source-data:/from:ro" -v "$patch-data:/to" --entrypoint sh $baselineImage -c 'cp -a /from/. /to/' 2>&1 | Out-Null
Write-Check 'patch.copy-of-stopped-source' ($LASTEXITCODE -eq 0) "$source-data -> $patch-data"
docker start $source 2>&1 | Out-Null
$patchStarted = Get-Date
& docker run -d --name $patch --network $network -e POSTGRES_USER -e POSTGRES_PASSWORD -v "$patch-data:/var/lib/postgresql/data" $targetImage | Out-Null
Write-Check 'patch.16.15-starts-on-16.11-files' (Wait-Postgres $patch @($appDb, $identityDb)) "seconds=$([math]::Round(((Get-Date) - $patchStarted).TotalSeconds, 1))"
$patchLog = docker logs $patch 2>&1 | Out-String
Save '05-patch-postgres.log' ($patchLog -replace [regex]::Escape($user), '<user>')
Write-Check 'patch.no-warnings' ($patchLog -match 'Skipping initialization' -and $patchLog -notmatch 'WARNING|collation version mismatch|PANIC|FATAL') ''
Write-Check 'patch.runtime-16.15' ((Get-ServerInfo $patch) -match 'PostgreSQL 16\.15 ') ''
$patchSnapshot = Get-Snapshot $patch
Save '05-patch-snapshot.txt' $patchSnapshot
Write-Check 'patch.structure-and-data-identical' ($patchSnapshot -ceq $sourceSnapshot) ''
Start-WebApi "p16-api-pat-$RunId" $patch
Write-Check 'patch.app-ready' (Wait-HttpOk '/health/ready') ''
Write-Check 'patch.identity-authenticate' ((Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = $seedPassword } | ConvertTo-Json -Compress)).Status -eq 200) ''
Write-Check 'patch.no-migration-applied' ((docker logs "p16-api-pat-$RunId" 2>&1 | Out-String) -notmatch 'Applying migration') ''

# ---------------------------------------------------------------- 6. Timed switch back to the preserved 16.11 baseline
# The application is stopped (write freeze), its connection is pointed back at the untouched 16.11 server, and the
# clock stops when readiness reports both databases healthy. No data directory is ever downgraded.
$rollbackStarted = Get-Date
docker stop -t 30 "p16-api-pat-$RunId" 2>&1 | Out-Null
$sourceReady = Wait-Postgres $source @($appDb, $identityDb)
Start-WebApi "p16-api-rb-$RunId" $source
$rollbackReady = Wait-HttpOk '/health/ready'
$timings['rollback-seconds'] = [math]::Round(((Get-Date) - $rollbackStarted).TotalSeconds, 1)
Write-Check 'rollback.ready-on-16.11' ($sourceReady -and $rollbackReady) "seconds=$($timings['rollback-seconds'])"
Write-Check 'rollback.runtime-16.11' ((Get-ServerInfo $source) -match 'PostgreSQL 16\.11 ') ''
Write-Check 'rollback.source-untouched-since-backup' ((Get-Snapshot $source) -ceq $sourceSnapshot) ''
Write-Check 'rollback.identity-authenticate' ((Get-Status 'POST' '/api/v2/account/authenticate' (@{ userName = $seedUser; password = $seedPassword } | ConvertTo-Json -Compress)).Status -eq 200) ''
Write-Check 'rollback.product-v2' ((Get-Status 'GET' "/api/v2/product/$keepId").Status -eq 200) ''
$sourceVolumeImages = @(docker ps -a --filter "volume=$source-data" --format '{{.Image}}' | Sort-Object -Unique)
Write-Check 'safety.source-volume-only-opened-by-16.11' (@($sourceVolumeImages | Where-Object { $_ -notmatch '^postgres:16\.11(@|$)' }).Count -eq 0) ($sourceVolumeImages -join ',')

# ---------------------------------------------------------------- 7. Stop (never remove) and summarize
docker stop -t 30 "p16-api-rb-$RunId" $source $restore $patch 2>&1 | Out-Null
$retained = @(docker volume ls --format '{{.Name}}' | Where-Object { $_ -like "p16-*-$RunId-data" } | Sort-Object)
Write-Check 'safety.volumes-retained' ($retained.Count -eq 3) ($retained -join ',')
Write-Check 'safety.backups-retained' (@(Get-ChildItem $backupDirectory -File).Count -eq 3) ''
Write-Check 'safety.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''
$evidence = Get-ChildItem $outputDirectory -Recurse -File -Include *.txt, *.log, *.sql | Select-String -SimpleMatch $env:POSTGRES_PASSWORD
Write-Check 'safety.no-password-in-evidence' (-not $evidence) ''
$jwtEvidence = Get-ChildItem $outputDirectory -Recurse -File -Include *.txt, *.log, *.sql | Select-String -SimpleMatch $env:JwtSettings__Key
Write-Check 'safety.no-jwt-key-in-evidence' (-not $jwtEvidence) ''

Save 'timings.txt' ($timings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
foreach ($name in 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'PGPASSWORD', 'POSTGRES_DB_NAME', 'POSTGRES_IDENTITY_DB_NAME', 'ConnectionStrings__DefaultConnection', 'ConnectionStrings__IdentityConnection', 'JwtSettings__Key') {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}
$failed = @($results | Where-Object { -not $_.Passed })
"run=$RunId checks=$($results.Count) failed=$($failed.Count) $(($timings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' ')" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
