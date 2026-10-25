<#
.SYNOPSIS
    Phase 33 backup/restore rehearsal (NFR-REC-1: RPO <= 24 h, RTO <= 4 h) for the application database, the Identity
    database and the private blob container.

.DESCRIPTION
    Source: the running local Compose stack (synthetic demo data). Backups are read-only: `pg_dump -Fc` of both
    databases and a tar of the Azurite data directory, each with SHA-256. Restore targets are new resources named
    after the RunId and never published on a host port:
      p33-pg-<RunId>      postgres:16.15 (the Compose pin) restored with pg_restore --create
      p33-blob-<RunId>    azurite:3.37.0 (the Compose pin) on the restored data directory
    Checks: every table's row count equals the source per database, every blob file's SHA-256 equals the source, the
    restored Azurite starts, and the measured backup + restore time stays within the RTO. Nothing is deleted or
    overwritten; the rehearsal ends with `docker stop`. Evidence goes to artifacts/validation/recovery-rehearsal/<RunId>.
    Credentials are read from the git-ignored .env and passed only to containers; nothing secret is printed.
#>
[CmdletBinding()]
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [string]$EnvFile,
    [string]$PostgresContainer = 'templatecleanarch-postgres',
    [string]$BlobContainer = 'templatecleanarch-files'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $EnvFile) { $EnvFile = Join-Path $repositoryRoot '.env' }
$evidence = Join-Path $repositoryRoot "artifacts\validation\recovery-rehearsal\$RunId"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$checks = New-Object System.Collections.Generic.List[object]
$postgresImage = 'postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d'
$azuriteImage = 'mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1da1a2d537e08f3e6764dd1f5ae00cf0346bcaf625b968ec3f0971fd5'

function Read-EnvValue([string]$Name) {
    $line = Get-Content -LiteralPath $EnvFile | Where-Object { $_ -match "^$Name=" } | Select-Object -First 1
    if (-not $line) { throw "$Name is missing in the env file." }
    return $line.Substring($Name.Length + 1)
}

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $checks.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    Write-Host ('[{0}] {1} {2}' -f ($(if ($Passed) { 'PASS' } else { 'FAIL' })), $Name, $Detail)
}

function Invoke-Docker {
    $output = & docker @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "docker $($args[0]) failed: $output" }
    return $output
}

function Get-RowCounts([string]$Container, [string]$User, [string]$Database) {
    # Exact counts of every user table (no payload is read), one "table|count" line each.
    $query = @'
DO $$
DECLARE r record; n bigint;
BEGIN
  CREATE TEMP TABLE _counts(t text, n bigint);
  FOR r IN SELECT schemaname, tablename FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname <> 'information_schema' LOOP
    EXECUTE format('SELECT count(*) FROM %I.%I', r.schemaname, r.tablename) INTO n;
    INSERT INTO _counts VALUES (r.schemaname || '.' || r.tablename, n);
  END LOOP;
END $$;
SELECT t || '|' || n FROM _counts ORDER BY t;
'@
    $result = $query | docker exec -i $Container psql -U $User -d $Database -v ON_ERROR_STOP=1 -At -f -
    if ($LASTEXITCODE -ne 0) { throw "Row counts failed for $Database." }
    return @($result | Where-Object { $_ -match '\|' })
}

$user = Read-EnvValue 'POSTGRES_USER'
$password = Read-EnvValue 'POSTGRES_PASSWORD'
$databases = @((Read-EnvValue 'POSTGRES_DB_NAME'), (Read-EnvValue 'POSTGRES_IDENTITY_DB_NAME'))
$restore = "p33-pg-$RunId"
$blob = "p33-blob-$RunId"
$blobVolume = "p33-blob-$RunId-data"
$clock = [Diagnostics.Stopwatch]::StartNew()

# 1. Backups (read-only).
$backupStarted = $clock.Elapsed
$sourceCounts = @{}
foreach ($database in $databases) {
    $sourceCounts[$database] = Get-RowCounts $PostgresContainer $user $database
    $file = Join-Path $evidence "$database.dump"
    Invoke-Docker exec $PostgresContainer pg_dump -U $user -d $database -Fc -f "/tmp/p33-$RunId-$database.dump" | Out-Null
    Invoke-Docker cp "${PostgresContainer}:/tmp/p33-$RunId-$database.dump" $file | Out-Null
    Invoke-Docker exec $PostgresContainer rm -f "/tmp/p33-$RunId-$database.dump" | Out-Null  # only the file this run wrote
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    "$hash  $database.dump" | Add-Content (Join-Path $evidence 'SHA256SUMS')
    Write-Check "backup.$database" ((Get-Item $file).Length -gt 0) ("{0:N0} bytes, {1} tables" -f (Get-Item $file).Length, $sourceCounts[$database].Count)
}
$blobTar = Join-Path $evidence 'blob-data.tar'
Invoke-Docker exec $BlobContainer tar -cf "/tmp/p33-$RunId-blob.tar" -C /data . | Out-Null
Invoke-Docker cp "${BlobContainer}:/tmp/p33-$RunId-blob.tar" $blobTar | Out-Null
Invoke-Docker exec $BlobContainer rm -f "/tmp/p33-$RunId-blob.tar" | Out-Null  # only the file this run wrote
"$((Get-FileHash -LiteralPath $blobTar -Algorithm SHA256).Hash)  blob-data.tar" | Add-Content (Join-Path $evidence 'SHA256SUMS')
$sourceBlobHashes = @(Invoke-Docker exec $BlobContainer sh -c "cd /data && find . -type f ! -name '*.json' -exec sha256sum {} + | sort -k2")
Write-Check 'backup.blob' ((Get-Item $blobTar).Length -gt 0) ("{0} blob files" -f $sourceBlobHashes.Count)
$backupDuration = $clock.Elapsed - $backupStarted

# 2. Restore into new, unpublished containers.
$restoreStarted = $clock.Elapsed
Invoke-Docker run -d --name $restore -e "POSTGRES_USER=$user" -e "POSTGRES_PASSWORD=$password" $postgresImage | Out-Null
foreach ($attempt in 1..60) {
    docker exec $restore pg_isready -U $user 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep -Seconds 1
}
foreach ($database in $databases) {
    Invoke-Docker cp (Join-Path $evidence "$database.dump") "${restore}:/tmp/$database.dump" | Out-Null
    Invoke-Docker exec $restore pg_restore -U $user -d postgres --create --exit-on-error "/tmp/$database.dump" | Out-Null
}
Invoke-Docker volume create $blobVolume | Out-Null
Invoke-Docker create --name $blob -v "${blobVolume}:/data" $azuriteImage azurite-blob --blobHost 0.0.0.0 --location /data --skipApiVersionCheck | Out-Null
Invoke-Docker cp $blobTar "${blob}:/tmp/blob-data.tar" | Out-Null
Invoke-Docker start $blob | Out-Null
Invoke-Docker exec -u root $blob tar -xf /tmp/blob-data.tar -C /data | Out-Null
Invoke-Docker restart $blob | Out-Null
$restoreDuration = $clock.Elapsed - $restoreStarted

# 3. Verification.
foreach ($database in $databases) {
    $restoredCounts = Get-RowCounts $restore $user $database
    $sourceCounts[$database] | Set-Content (Join-Path $evidence "$database.source-counts.txt")
    $restoredCounts | Set-Content (Join-Path $evidence "$database.restored-counts.txt")
    $equal = ($sourceCounts[$database] -join "`n") -ceq ($restoredCounts -join "`n")
    $rows = ($restoredCounts | ForEach-Object { [long]($_ -split '\|')[1] } | Measure-Object -Sum).Sum
    Write-Check "restore.$database.row-counts-equal" $equal ("{0} tables, {1} rows" -f $restoredCounts.Count, $rows)
}
$restoredBlobHashes = @(Invoke-Docker exec $blob sh -c "cd /data && find . -type f ! -name '*.json' -exec sha256sum {} + | sort -k2")
Write-Check 'restore.blob.sha256-equal' (($sourceBlobHashes -join "`n") -ceq ($restoredBlobHashes -join "`n")) ("{0} files" -f $restoredBlobHashes.Count)
$running = (Invoke-Docker inspect -f '{{.State.Running}}' $blob) -eq 'true'
Write-Check 'restore.blob.azurite-running' $running ''
$rto = $backupDuration + $restoreDuration
Write-Check 'rto.within-4h' ($rto -lt [TimeSpan]::FromHours(4)) ("backup {0:N1} s + restore {1:N1} s" -f $backupDuration.TotalSeconds, $restoreDuration.TotalSeconds)

# 4. Stop (never delete) the rehearsal resources.
Invoke-Docker stop $restore $blob | Out-Null
$checks | Export-Csv -NoTypeInformation -Path (Join-Path $evidence 'checks.csv')
$failed = @($checks | Where-Object { -not $_.Passed }).Count
Write-Host ("Recovery rehearsal {0}: {1}/{2} checks passed; stopped (not removed): {3}, {4}, volume {5}. Evidence: {6}" -f `
    $RunId, ($checks.Count - $failed), $checks.Count, $restore, $blob, $blobVolume, $evidence)
if ($failed -gt 0) { exit 1 }
