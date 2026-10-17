<#
.SYNOPSIS
    Phase 17 isolated RabbitMQ upgrade rehearsal: 4.2.2 (Phase 14 pin) -> 4.2.9 (latest public 4.2 patch) -> all stable
    feature flags -> 4.3.6, with definitions export/import parity, persistent sentinel messages, application
    publish/consume on the target, broker restart/recreation, shutdown, and a timed switch back to the untouched source.
.DESCRIPTION
    Officially supported path (rabbitmq.com/docs/upgrade and the 4.3.0 release notes, verified 2026-10-12): 4.3.x can only
    be reached from the latest available 4.2.x patch, with every stable feature flag (including khepri_db) enabled first.
    Feature flags cannot be disabled and downgrades are unsupported, so every hop runs on a file copy of the stopped
    previous volume and rollback reconnects the application to the untouched source broker.

    Every resource is new and named after the RunId; only management ports and the WebApi are published, on loopback:
      network     p17-net-<RunId>
      source      p17-src-<RunId>   4.2.2 on p17-src-<RunId>-data (only ever opened by 4.2.2; rollback environment)
      hop         p17-hop-<RunId>   4.2.9 on p17-hop-<RunId>-data (copy of the stopped source; flags enabled here)
      target      p17-tgt-<RunId>   4.3.6 on p17-tgt-<RunId>-data (copy of the stopped hop), then p17-tgt2-<RunId>: a new
                                    container on the same volume and node name (container recreation)
      no-hostname p17-noh-<RunId>   4.3.6 on a copy of the target without a fixed hostname (shows why Compose pins it)
      import      p17-imp-<RunId>   fresh 4.3.6 importing the source definitions export
      webapi      p17-api-{src,tgt,rb}-<RunId> from image p17-webapi:<RunId> (in-memory database; cache, search and
                                    Elastic disabled) using the real messaging adapter
    Brokers share the node name rabbit@p17-rabbit (the data directory is bound to it); the import broker uses its own.
    Plugins are set explicitly to [rabbitmq_management]. Data is synthetic: the application's own messages (published
    with consumers disabled) plus sentinel messages with known message_id values. Credentials are random, passed to
    Docker through the process environment only and never printed. Messages are compared as
    message_id|delivery_mode|content_type|type|sha256(body); no payload is written. Definitions exports contain the
    synthetic user's password hash: the raw files stay in the access-restricted, git-ignored secure folder, and only
    sanitized copies are compared. The script never removes containers, volumes, images or networks (helper copy
    containers use --rm) and ends with `docker stop`. Evidence: artifacts/validation/rabbitmq-rehearsal/<RunId>.
#>
param(
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [int]$WebApiPort = 18170,
    [int]$ManagementPortBase = 18171,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$sourceImage = 'rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870'
$hopImage = 'rabbitmq:4.2.9-management@sha256:9e9fd21d60f060acc9ecbd847cd1dcb87e6550bdb0297c27e79a40bfe1766ce4'
$targetImage = 'rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd'
$network = "p17-net-$RunId"
$source = "p17-src-$RunId"; $hop = "p17-hop-$RunId"; $target = "p17-tgt-$RunId"; $target2 = "p17-tgt2-$RunId"
$noHostname = "p17-noh-$RunId"; $import = "p17-imp-$RunId"
$nodeHost = 'p17-rabbit'
$webApiImage = "p17-webapi:$RunId"
$managementPorts = @{ $source = $ManagementPortBase; $hop = $ManagementPortBase + 1; $target = $ManagementPortBase + 2; $target2 = $ManagementPortBase + 3; $import = $ManagementPortBase + 4; $noHostname = $ManagementPortBase + 5 }
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\rabbitmq-rehearsal\$RunId"
$secureDirectory = Join-Path $outputDirectory 'secure'
New-Item -ItemType Directory -Force -Path $secureDirectory | Out-Null
# Definitions exports hold a password hash: only the current user may read the secure folder.
icacls $secureDirectory /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" | Out-Null
$pluginsFile = Join-Path $outputDirectory 'enabled_plugins'
[System.IO.File]::WriteAllText($pluginsFile, "[rabbitmq_management].`n")
$baseUri = "http://127.0.0.1:$WebApiPort"
$results = New-Object System.Collections.Generic.List[object]
$timings = [ordered]@{}

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function New-Secret { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

# Synthetic credentials live only in this process environment; `docker run -e NAME` copies them without printing.
$env:RABBITMQ_DEFAULT_USER = "p17_$((New-Secret).Substring(0, 8))"
$env:RABBITMQ_DEFAULT_PASS = New-Secret
$env:RabbitMqSettings__UserName = $env:RABBITMQ_DEFAULT_USER
$env:RabbitMqSettings__Password = $env:RABBITMQ_DEFAULT_PASS
# Phase 22: per-run JWT signing key from a cryptographically secure source; `docker run -e NAME` copies it unprinted.
$jwtKeyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($jwtKeyBytes)
$env:JwtSettings__Key = [Convert]::ToBase64String($jwtKeyBytes)
$user = $env:RABBITMQ_DEFAULT_USER
$authorization = 'Basic ' + [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("${user}:$($env:RABBITMQ_DEFAULT_PASS)"))

function Mask([string]$Text) { return ($Text -replace [regex]::Escape($user), '<user>') }

function Save([string]$Name, $Content) { Mask (($Content | Out-String).Trim()) | Set-Content -Path (Join-Path $outputDirectory $Name) -Encoding utf8 }

function Invoke-Ctl([string]$Container, [string[]]$Arguments) {
    $output = & docker exec $Container @Arguments 2>&1
    # Only line breaks are trimmed: a leading tab is an empty first column (the default exchange in list_bindings).
    return (Mask (($output | Out-String).Trim("`r", "`n")))
}

function Start-Broker([string]$Name, [string]$Image, [string]$Volume, [string]$HostName) {
    $arguments = @('run', '-d', '--name', $Name, '--network', $network, '--network-alias', $Name,
        '-e', 'RABBITMQ_DEFAULT_USER', '-e', 'RABBITMQ_DEFAULT_PASS',
        '-v', "${Volume}:/var/lib/rabbitmq", '-v', "${pluginsFile}:/etc/rabbitmq/enabled_plugins:ro",
        '-p', "127.0.0.1:$($managementPorts[$Name]):15672")
    if ($HostName) { $arguments += @('--hostname', $HostName) }
    $arguments += $Image
    & docker @arguments | Out-Null
}

# The CLI must not run before the node created its Erlang cookie, so readiness first waits for the startup log line.
function Wait-Broker([string]$Name, [string]$Since) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $log = (& docker logs --since $Since $Name 2>&1 | Out-String)
        if ($log -match 'Server startup complete') {
            & docker exec $Name rabbitmq-diagnostics -q check_running 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { return $true }
        }
        if ((& docker inspect $Name --format '{{.State.Running}}') -ne 'true') { return $false }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Stop-Container([string]$Name, [int]$Seconds = 60) { & docker stop -t $Seconds $Name 2>&1 | Out-Null }

function Now { return (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') }

function Copy-Volume([string]$From, [string]$To) {
    # Only between stopped brokers: RabbitMQ data must not be copied while a node is running.
    & docker run --rm -v "${From}:/from:ro" -v "${To}:/to" --entrypoint sh $sourceImage -c 'cp -a /from/. /to/' 2>&1 | Out-Null
    return $LASTEXITCODE -eq 0
}

function Invoke-Management([string]$Name, [string]$Method, [string]$Path, $Body) {
    $parameters = @{ Uri = "http://127.0.0.1:$($managementPorts[$Name])/api$Path"; Method = $Method; Headers = @{ Authorization = $authorization }; TimeoutSec = 30 }
    if ($null -ne $Body) { $parameters.Body = ($Body | ConvertTo-Json -Depth 10 -Compress); $parameters.ContentType = 'application/json' }
    return Invoke-RestMethod @parameters
}

function Publish-Sentinel([string]$Name, [string]$Exchange, [string]$RoutingKey, [string]$MessageId, [string]$Payload) {
    $body = @{ properties = @{ delivery_mode = 2; message_id = $MessageId; content_type = 'application/json'; type = 'order.placed.v1' }
        routing_key = $RoutingKey; payload = $Payload; payload_encoding = 'string' }
    $exchangePath = if ($Exchange) { $Exchange } else { 'amq.default' }
    return (Invoke-Management $Name 'POST' "/exchanges/%2F/$exchangePath/publish" $body).routed
}

function Get-Sha256([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return (-join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text)) | ForEach-Object { $_.ToString('x2') }))
}

$orderQueues = 'queue_fanout_a', 'queue_fanout_b', 'urgent_orders', 'orders_brazil'
$allQueues = $orderQueues + ($orderQueues | ForEach-Object { "$_.dead_letter" })

# Message inventory: get with requeue, so the broker keeps every message; only properties and a body digest are kept.
function Get-MessageInventory([string]$Name) {
    $lines = foreach ($queue in $allQueues) {
        # Windows PowerShell returns a JSON array as one object; the pipeline unrolls it.
        $messages = @(Invoke-Management $Name 'POST' "/queues/%2F/$queue/get" @{ count = 1000; ackmode = 'ack_requeue_true'; encoding = 'auto' } | ForEach-Object { $_ })
        foreach ($message in $messages) {
            if ($null -eq $message -or $null -eq $message.properties) { continue }
            "$queue|$($message.properties.message_id)|$($message.properties.delivery_mode)|$($message.properties.content_type)|$($message.properties.type)|$(Get-Sha256 $message.payload)"
        }
    }
    return (($lines | Sort-Object) -join "`n")
}

function Get-Topology([string]$Name) {
    $sections = [ordered]@{
        exchanges = @('rabbitmqctl', '-q', '-s', 'list_exchanges', '-p', '/', 'name', 'type', 'durable', 'auto_delete', 'internal', 'arguments')
        queues = @('rabbitmqctl', '-q', '-s', 'list_queues', '-p', '/', 'name', 'type', 'durable', 'exclusive', 'auto_delete', 'arguments')
        bindings = @('rabbitmqctl', '-q', '-s', 'list_bindings', '-p', '/', 'source_name', 'source_kind', 'destination_name', 'destination_kind', 'routing_key', 'arguments')
        vhosts = @('rabbitmqctl', '-q', '-s', 'list_vhosts', 'name')
        users = @('rabbitmqctl', '-q', '-s', 'list_users')
        permissions = @('rabbitmqctl', '-q', '-s', 'list_permissions', '-p', '/')
        topic_permissions = @('rabbitmqctl', '-q', '-s', 'list_topic_permissions', '-p', '/')
        policies = @('rabbitmqctl', '-q', '-s', 'list_policies', '-p', '/')
        operator_policies = @('rabbitmqctl', '-q', '-s', 'list_operator_policies', '-p', '/')
        vhost_limits = @('rabbitmqctl', '-q', '-s', 'list_vhost_limits', '-p', '/')
        parameters = @('rabbitmqctl', '-q', '-s', 'list_parameters', '-p', '/')
    }
    $text = foreach ($section in $sections.Keys) {
        "[$section]"
        # Broker-owned amq.rabbitmq.* exchanges are version-specific (4.3 declares amq.rabbitmq.log); they are recorded
        # separately and left out of the application topology comparison.
        ((Invoke-Ctl $Name $sections[$section]) -split "`r?`n" | Where-Object { $_ -and $_ -notmatch '^amq\.rabbitmq\.' } | Sort-Object) -join "`n"
    }
    return ($text -join "`n")
}

function Get-QueueCounts([string]$Name) {
    $lines = (Invoke-Ctl $Name @('rabbitmqctl', '-q', '-s', 'list_queues', '-p', '/', 'name', 'messages_ready', 'messages_unacknowledged')) -split "`r?`n"
    $counts = @{}
    foreach ($line in $lines) { $parts = $line -split '\t'; if ($parts.Count -ge 3) { $counts[$parts[0]] = [int]$parts[1] + [int]$parts[2] } }
    return $counts
}

function Get-FeatureFlags([string]$Name) { return (Invoke-Ctl $Name @('rabbitmqctl', '-q', '-s', 'list_feature_flags', 'name', 'state', 'stability')) }

function Get-DisabledStableFlags([string]$Flags) {
    return @($Flags -split "`r?`n" | Where-Object { $_ -match '^(\S+)\t(\S+)\t(stable|required)$' -and $Matches[2] -ne 'enabled' } | ForEach-Object { ($_ -split '\t')[0] })
}

function Export-Definitions([string]$Name, [string]$FileName) {
    & docker exec $Name rabbitmqctl -q export_definitions /tmp/p17-definitions.json 2>&1 | Out-Null
    $exported = $LASTEXITCODE -eq 0
    & docker cp "${Name}:/tmp/p17-definitions.json" (Join-Path $secureDirectory $FileName) 2>&1 | Out-Null
    & docker exec $Name rm -f /tmp/p17-definitions.json 2>&1 | Out-Null
    return $exported -and (Test-Path (Join-Path $secureDirectory $FileName))
}

# Semantic, secret-free view of a definitions export: users without hashes, sorted entities, no version fields.
function Get-SanitizedDefinitions([string]$FileName) {
    $definitions = Get-Content (Join-Path $secureDirectory $FileName) -Raw | ConvertFrom-Json
    $sanitized = [ordered]@{
        users = @($definitions.users | ForEach-Object { [ordered]@{ name = '<user>'; tags = @($_.tags) -join ','; has_hash = [bool]$_.password_hash; hashing = $_.hashing_algorithm } })
        vhosts = @($definitions.vhosts | ForEach-Object { $_.name } | Sort-Object)
        permissions = @($definitions.permissions | ForEach-Object { "$($_.vhost)|$($_.configure)|$($_.write)|$($_.read)" } | Sort-Object)
        topic_permissions = @($definitions.topic_permissions | ForEach-Object { $_ | ConvertTo-Json -Compress } | Sort-Object)
        parameters = @($definitions.parameters | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 10 } | Sort-Object)
        policies = @($definitions.policies | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 10 } | Sort-Object)
        queues = @($definitions.queues | ForEach-Object { "$($_.vhost)|$($_.name)|$($_.durable)|$($_.auto_delete)|$($_.arguments | ConvertTo-Json -Compress)" } | Sort-Object)
        exchanges = @($definitions.exchanges | Where-Object { $_.name -notlike 'amq.rabbitmq.*' } | ForEach-Object { "$($_.vhost)|$($_.name)|$($_.type)|$($_.durable)|$($_.auto_delete)|$($_.internal)|$($_.arguments | ConvertTo-Json -Compress)" } | Sort-Object)
        bindings = @($definitions.bindings | ForEach-Object { "$($_.vhost)|$($_.source)|$($_.destination)|$($_.destination_type)|$($_.routing_key)|$($_.arguments | ConvertTo-Json -Compress)" } | Sort-Object)
    }
    return ($sanitized | ConvertTo-Json -Depth 5)
}

function Start-WebApi([string]$Name, [string]$BrokerHost, [bool]$Consumers) {
    $arguments = @('run', '-d', '--name', $Name, '--network', $network, '-p', "127.0.0.1:${WebApiPort}:8080",
        '-e', 'ASPNETCORE_ENVIRONMENT=Production', '-e', 'ASPNETCORE_HTTP_PORTS=8080', '-e', 'UseInMemoryDatabase=true',
        '-e', 'CacheSettings__Enabled=false', '-e', 'PersonSearchSettings__Enabled=false',
        '-e', 'ElasticLoggingSettings__Enabled=false', '-e', 'ElasticsearchHealthCheckSettings__Enabled=false',
        '-e', "RabbitMqSettings__HostName=$BrokerHost", '-e', 'RabbitMqSettings__Port=5672',
        '-e', 'RabbitMqSettings__UserName', '-e', 'RabbitMqSettings__Password', '-e', 'JwtSettings__Key',
        '-e', "RabbitMqSettings__ConsumersEnabled=$($Consumers.ToString().ToLowerInvariant())", $webApiImage)
    & docker @arguments | Out-Null
}

function Get-Status([string]$Method, [string]$Path, [string]$Body) {
    try {
        $parameters = @{ Uri = "$baseUri$Path"; Method = $Method; UseBasicParsing = $true; TimeoutSec = 30 }
        if ($Body) { $parameters.Body = $Body; $parameters.ContentType = 'application/json' }
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return [pscustomobject]@{ Status = $status; Content = '' }
    }
}

# Ready means /health/ready 200 with the optional message-broker check Healthy (TCP reachability, Phase 13).
function Wait-WebApiBrokerReady([int]$Seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $ready = Get-Status 'GET' '/health/ready'
        if ($ready.Status -eq 200 -and $ready.Content -match '"name":"message-broker","status":"Healthy"') { return $ready.Content }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Publish-Order([string]$Endpoint) {
    $order = @{ id = [guid]::Empty; productName = 'P17Synthetic'; quantity = 1; price = 1.5 } | ConvertTo-Json -Compress
    return (Get-Status 'POST' "/api/v1/message/$Endpoint" $order).Status
}

function Wait-Counts([string]$Name, [hashtable]$Expected, [int]$Seconds = 60) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $counts = Get-QueueCounts $Name
        $mismatch = @($Expected.Keys | Where-Object { $counts[$_] -ne $Expected[$_] })
        if ($mismatch.Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-NormalVolumes {
    $names = docker volume ls --format '{{.Name}}' | Where-Object { $_ -like 'cleanarchitecture_*' -or $_ -like 'templatecleanarchnet_*' }
    return ($names | ForEach-Object { "$_@" + (docker volume inspect $_ --format '{{.CreatedAt}}') } | Sort-Object) -join ';'
}

# ---------------------------------------------------------------- 0. Isolation guard, images and application image
$existing = @(docker ps -a --format '{{.Names}}' | Where-Object { $_ -like "p17-*-$RunId" })
$existingVolumes = @(docker volume ls --format '{{.Name}}' | Where-Object { $_ -like "p17-*-$RunId-data" })
if ($existing.Count -gt 0 -or $existingVolumes.Count -gt 0) { throw "RunId $RunId already has p17 resources; choose another RunId." }
$normalVolumesBefore = Get-NormalVolumes
Write-Check 'isolation.new-run' $true "run=$RunId"

foreach ($image in $sourceImage, $hopImage, $targetImage) {
    & docker pull -q $image 2>&1 | Out-Null
    $platform = & docker image inspect $image --format '{{.Os}}/{{.Architecture}}' 2>$null
    Write-Check "image.available.$(($image -split '@')[0])" ($LASTEXITCODE -eq 0 -and $platform -eq 'linux/amd64') $platform
}
& docker network create $network 2>&1 | Out-Null
$built = Get-Date
& docker build -q -t $webApiImage -f Src/Presentation/Template.WebApi/Dockerfile . 2>&1 | Set-Content (Join-Path $outputDirectory '00-webapi-build.log')
Write-Check 'webapi.image-built' ($LASTEXITCODE -eq 0) "seconds=$([int]((Get-Date) - $built).TotalSeconds)"

# ---------------------------------------------------------------- 1. Source 4.2.2 with the application's topology
$since = Now
Start-Broker $source $sourceImage "$source-data" $nodeHost
Write-Check 'source.started' (Wait-Broker $source $since) $source
$sourceVersion = Invoke-Ctl $source @('rabbitmq-diagnostics', '-q', 'server_version')
$sourceErlang = Invoke-Ctl $source @('rabbitmq-diagnostics', '-q', 'erlang_version')
Write-Check 'source.version-4.2.2' ($sourceVersion -eq '4.2.2') "version=$sourceVersion erlang=$sourceErlang"

Start-WebApi "p17-api-src-$RunId" $source $false
Write-Check 'source.webapi-ready' ($null -ne (Wait-WebApiBrokerReady)) 'consumers disabled'
$published = @(
    Publish-Order 'create-order-fanout'
    Publish-Order 'create-order-direct?routingKey=order.urgent'
    Publish-Order 'create-order-topic?routingKey=order.brazil.sp'
    Publish-Order 'create-order-direct?routingKey=order.unrouted'
)
Write-Check 'source.application-published' (@($published | Where-Object { $_ -ne 200 }).Count -eq 0) ($published -join ',')

# Sentinels with known message_id values: two per order queue and one already dead-lettered message.
$sentinels = New-Object System.Collections.Generic.List[string]
$sentinelRouted = $true
foreach ($queue in $orderQueues) {
    foreach ($index in 1, 2) {
        $id = [guid]::NewGuid().ToString('D')
        $payload = '{"Id":"' + $id + '","ProductName":"P17Sentinel","Quantity":' + $index + ',"Price":1.00}'
        $sentinelRouted = $sentinelRouted -and (Publish-Sentinel $source '' $queue $id $payload)
        $sentinels.Add("$queue|$id")
    }
}
$deadLetterId = [guid]::NewGuid().ToString('D')
$sentinelRouted = $sentinelRouted -and (Publish-Sentinel $source 'order_dead_letter_exchange' 'urgent_orders' $deadLetterId ('{"Id":"' + $deadLetterId + '","ProductName":"P17DeadLetter","Quantity":1,"Price":1.00}'))
$sentinels.Add("urgent_orders.dead_letter|$deadLetterId")
Write-Check 'source.sentinels-published' $sentinelRouted "count=$($sentinels.Count)"
Save '01-sentinel-ids.txt' ($sentinels -join "`n")

$expectedCounts = @{ 'queue_fanout_a' = 3; 'queue_fanout_b' = 3; 'urgent_orders' = 3; 'orders_brazil' = 3; 'urgent_orders.dead_letter' = 1;
    'queue_fanout_a.dead_letter' = 0; 'queue_fanout_b.dead_letter' = 0; 'orders_brazil.dead_letter' = 0 }
Write-Check 'source.expected-message-counts' (Wait-Counts $source $expectedCounts) ''

# Write freeze: the publisher stops before the inventory and the backup point.
Stop-Container "p17-api-src-$RunId" 30
$sourceMessages = Get-MessageInventory $source
Save '02-source-messages.txt' $sourceMessages
$sentinelsPresent = @($sentinels | Where-Object { $sourceMessages -notmatch [regex]::Escape(($_ -split '\|')[0] + '|' + ($_ -split '\|')[1] + '|2|application/json|order.placed.v1|') }).Count -eq 0
Write-Check 'source.sentinels-persistent-with-v1-properties' $sentinelsPresent ''
Write-Check 'source.application-messages-persistent' (@($sourceMessages -split "`n" | Where-Object { $_ -notmatch '\|2\|application/json\|order\.placed\.v1\|[0-9a-f]{64}$' }).Count -eq 0) ''

$sourceTopology = Get-Topology $source
Save '03-source-topology.txt' $sourceTopology
$sourceFlags = Get-FeatureFlags $source
Save '04-source-feature-flags.txt' $sourceFlags
$sourcePlugins = Invoke-Ctl $source @('rabbitmq-plugins', '-q', 'list', '-e', '-m')
Save '05-source-plugins.txt' $sourcePlugins
Write-Check 'source.plugins-explicit-management-only' ((($sourcePlugins -split "`r?`n" | Sort-Object) -join ',') -eq 'rabbitmq_management,rabbitmq_management_agent,rabbitmq_web_dispatch') ($sourcePlugins -replace "`r?`n", ',')
$deprecated = Invoke-Ctl $source @('rabbitmq-diagnostics', '-q', 'list_deprecated_features', '--used')
Save '06-source-deprecated-features-used.txt' $deprecated
Write-Check 'source.no-deprecated-feature-denied-in-4.3' ($deprecated -notmatch 'transient_nonexcl_queues|global_qos|queue_master_locator|classic_queue_mirroring|ram_node_type|amqp_address_v1') ($deprecated -replace "`r?`n", ' ')
Write-Check 'source.no-classic-queue-v1' ($sourceTopology -notmatch 'x-queue-version|x-queue-mode') ''
foreach ($command in 'status', 'cluster_status', 'alarms', 'listeners', 'environment') {
    Save "07-source-$command.txt" (Invoke-Ctl $source @('rabbitmq-diagnostics', '-q', $command))
}
Write-Check 'source.definitions-exported' (Export-Definitions $source 'definitions-source-4.2.2.json') ''
$sourceDefinitions = Get-SanitizedDefinitions 'definitions-source-4.2.2.json'
Save '08-source-definitions.sanitized.json' $sourceDefinitions
Write-Check 'source.definitions-complete' ($sourceDefinitions -match 'urgent_orders.dead_letter' -and $sourceDefinitions -match 'order_dead_letter_exchange' -and $sourceDefinitions -match '"has_hash":\s*true') ''

$stopped = Get-Date
Stop-Container $source
$timings['source-clean-stop-seconds'] = [math]::Round(((Get-Date) - $stopped).TotalSeconds, 1)
Write-Check 'source.clean-stop' ((& docker inspect $source --format '{{.State.ExitCode}}') -eq '0') "seconds=$($timings['source-clean-stop-seconds'])"

# ---------------------------------------------------------------- 2. Hop: 4.2.9 on a copy, then every stable flag
Write-Check 'hop.volume-copied-from-stopped-source' (Copy-Volume "$source-data" "$hop-data") ''
$since = Now
Start-Broker $hop $hopImage "$hop-data" $nodeHost
Write-Check 'hop.started' (Wait-Broker $hop $since) $hop
$hopVersion = Invoke-Ctl $hop @('rabbitmq-diagnostics', '-q', 'server_version')
Write-Check 'hop.version-4.2.9' ($hopVersion -eq '4.2.9') "version=$hopVersion erlang=$(Invoke-Ctl $hop @('rabbitmq-diagnostics', '-q', 'erlang_version'))"
Write-Check 'hop.topology-preserved' ((Get-Topology $hop) -ceq $sourceTopology) ''
Write-Check 'hop.messages-preserved' ((Get-MessageInventory $hop) -ceq $sourceMessages) ''
$hopFlagsBefore = Get-FeatureFlags $hop
Save '09-hop-feature-flags-before.txt' $hopFlagsBefore
$output = Invoke-Ctl $hop @('rabbitmqctl', '-q', 'enable_feature_flag', 'all')
Write-Check 'hop.enable-all-stable-feature-flags' ($LASTEXITCODE -eq 0) ($output -replace "`r?`n", ' ')
$hopFlags = Get-FeatureFlags $hop
Save '10-hop-feature-flags-after.txt' $hopFlags
$disabledOnHop = Get-DisabledStableFlags $hopFlags
Write-Check 'hop.all-stable-flags-enabled-including-khepri' ($disabledOnHop.Count -eq 0 -and $hopFlags -match '(?m)^khepri_db\tenabled') ("disabled=" + ($disabledOnHop -join ','))
Stop-Container $hop
Write-Check 'hop.clean-stop' ((& docker inspect $hop --format '{{.State.ExitCode}}') -eq '0') ''

# ---------------------------------------------------------------- 3. Target 4.3.6 on a copy of the stopped hop
Write-Check 'target.volume-copied-from-stopped-hop' (Copy-Volume "$hop-data" "$target-data") ''
$since = Now
$upgradeStarted = Get-Date
Start-Broker $target $targetImage "$target-data" $nodeHost
Write-Check 'target.started' (Wait-Broker $target $since) $target
$timings['target-upgrade-start-seconds'] = [math]::Round(((Get-Date) - $upgradeStarted).TotalSeconds, 1)
$targetVersion = Invoke-Ctl $target @('rabbitmq-diagnostics', '-q', 'server_version')
$targetErlang = Invoke-Ctl $target @('rabbitmq-diagnostics', '-q', 'erlang_version')
Write-Check 'target.version-4.3.6' ($targetVersion -eq '4.3.6') "version=$targetVersion"
Write-Check 'target.erlang-otp-27' ($targetErlang -match 'OTP 27') $targetErlang
foreach ($check in 'check_running', 'check_local_alarms', 'check_port_connectivity', 'check_virtual_hosts') {
    Invoke-Ctl $target @('rabbitmq-diagnostics', '-q', $check) | Out-Null
    Write-Check "target.health.$check" ($LASTEXITCODE -eq 0) ''
}
$upgradeLog = (& docker logs --since $since $target 2>&1 | Out-String)
Save '11-target-upgrade-log-warnings.txt' (($upgradeLog -split "`r?`n" | Where-Object { $_ -match '\[(warning|error)\]' }) -join "`n")
Write-Check 'target.no-error-in-upgrade-log' ($upgradeLog -notmatch '\[error\]') ''
$targetFlagsAfterUpgrade = Get-FeatureFlags $target
Save '12-target-feature-flags-after-upgrade.txt' $targetFlagsAfterUpgrade
$newStableFlags = Get-DisabledStableFlags $targetFlagsAfterUpgrade
# Official guidance: once every node runs the new version, enable the stable flags it introduced (irreversible).
$output = Invoke-Ctl $target @('rabbitmqctl', '-q', 'enable_feature_flag', 'all')
$targetFlags = Get-FeatureFlags $target
Save '13-target-feature-flags-final.txt' $targetFlags
Write-Check 'target.all-stable-flags-enabled' ((Get-DisabledStableFlags $targetFlags).Count -eq 0 -and $targetFlags -match '(?m)^khepri_db\tenabled') ("enabled-after-upgrade=" + ($newStableFlags -join ','))
$targetPlugins = Invoke-Ctl $target @('rabbitmq-plugins', '-q', 'list', '-e', '-m')
Save '14-target-plugins.txt' $targetPlugins
Write-Check 'target.plugins-explicit-management-only' ($targetPlugins -ceq $sourcePlugins) ($targetPlugins -replace "`r?`n", ',')
$overview = Invoke-Management $target 'GET' '/overview' $null
Write-Check 'target.management-api' ($overview.rabbitmq_version -eq '4.3.6') "management=$($overview.management_version)"
$targetTopology = Get-Topology $target
Save '15-target-topology.txt' $targetTopology
Write-Check 'target.topology-identical-to-source' ($targetTopology -ceq $sourceTopology) ''
$targetMessages = Get-MessageInventory $target
Write-Check 'target.sentinels-and-messages-preserved' ($targetMessages -ceq $sourceMessages) "messages=$(@($targetMessages -split "`n").Count)"
Write-Check 'target.definitions-exported' (Export-Definitions $target 'definitions-target-4.3.6.json') ''
$targetDefinitions = Get-SanitizedDefinitions 'definitions-target-4.3.6.json'
Save '16-target-definitions.sanitized.json' $targetDefinitions
Write-Check 'target.definitions-parity-with-source' ($targetDefinitions -ceq $sourceDefinitions) ''
$brokerExchanges = Invoke-Ctl $target @('rabbitmqctl', '-q', '-s', 'list_exchanges', '-p', '/', 'name', 'type', 'durable', 'internal')
Save '15-target-broker-owned-exchanges.txt' (($brokerExchanges -split "`r?`n" | Where-Object { $_ -match '^amq\.rabbitmq\.' }) -join "`n")
$deprecatedTarget = Invoke-Ctl $target @('rabbitmq-diagnostics', '-q', 'list_deprecated_features', '--used')
Save '17-target-deprecated-features-used.txt' $deprecatedTarget

$since = Now
& docker restart -t 60 $target 2>&1 | Out-Null
Write-Check 'target.restart' (Wait-Broker $target $since) ''
Write-Check 'target.restart-topology-preserved' ((Get-Topology $target) -ceq $sourceTopology) ''
Write-Check 'target.restart-messages-preserved' ((Get-MessageInventory $target) -ceq $sourceMessages) ''
Stop-Container $target

# Container recreation: a new container with the same node name and volume keeps the data.
$since = Now
Start-Broker $target2 $targetImage "$target-data" $nodeHost
Write-Check 'target.recreated-container-started' (Wait-Broker $target2 $since) $target2
Write-Check 'target.recreated-container-messages-preserved' ((Get-MessageInventory $target2) -ceq $sourceMessages) ''
Stop-Container $target2

# Without a fixed hostname the node name follows the container id, so a recreated container starts an empty node.
Write-Check 'nohostname.volume-copied-from-stopped-target' (Copy-Volume "$target-data" "$noHostname-data") ''
$since = Now
Start-Broker $noHostname $targetImage "$noHostname-data" $null
Write-Check 'nohostname.started' (Wait-Broker $noHostname $since) ''
$noHostnameQueues = Invoke-Ctl $noHostname @('rabbitmqctl', '-q', '-s', 'list_queues', '-p', '/', 'name')
Write-Check 'nohostname.starts-a-new-empty-node' ($noHostnameQueues -notmatch 'urgent_orders') 'documents why Compose pins the RabbitMQ hostname'
Stop-Container $noHostname

# ---------------------------------------------------------------- 4. Definitions import into a fresh 4.3.6 broker
$since = Now
Start-Broker $import $targetImage "$import-data" 'p17-import'
Write-Check 'import.started' (Wait-Broker $import $since) ''
& docker cp (Join-Path $secureDirectory 'definitions-source-4.2.2.json') "${import}:/tmp/p17-definitions.json" 2>&1 | Out-Null
$output = Invoke-Ctl $import @('rabbitmqctl', '-q', 'import_definitions', '/tmp/p17-definitions.json')
$imported = $LASTEXITCODE -eq 0
& docker exec -u root $import rm -f /tmp/p17-definitions.json 2>&1 | Out-Null
Write-Check 'import.definitions-imported' $imported ($output -replace "`r?`n", ' ')
$importTopology = Get-Topology $import
Save '18-import-topology.txt' $importTopology
Write-Check 'import.topology-identical-to-source' ($importTopology -ceq $sourceTopology) ''
$importCounts = Get-QueueCounts $import
Write-Check 'import.definitions-carry-no-messages' (@($allQueues | Where-Object { $importCounts[$_] -ne 0 }).Count -eq 0) 'messages need the data-directory path'
Write-Check 'import.imported-user-authenticates' ((Invoke-Management $import 'GET' '/whoami' $null).name -eq $user) ''
Stop-Container $import

# ---------------------------------------------------------------- 5. Application on the target
$since = Now
& docker start $target2 2>&1 | Out-Null
Write-Check 'target.started-for-application' (Wait-Broker $target2 $since) ''
Start-WebApi "p17-api-tgt-$RunId" $target2 $true
$readyBody = Wait-WebApiBrokerReady
Write-Check 'app.ready-with-broker-healthy' ($null -ne $readyBody) ''
Save '19-app-ready.json' $readyBody
$drained = @{ 'queue_fanout_a' = 0; 'queue_fanout_b' = 0; 'urgent_orders' = 0; 'orders_brazil' = 0; 'urgent_orders.dead_letter' = 1 }
Write-Check 'app.consumed-and-acked-preserved-messages' (Wait-Counts $target2 $drained) 'dead-letter sentinel retained'
$published = @(
    Publish-Order 'create-order-fanout'
    Publish-Order 'create-order-direct?routingKey=order.urgent'
    Publish-Order 'create-order-topic?routingKey=order.brazil.rj'
    Publish-Order 'create-order-direct?routingKey=order.unrouted'
)
Write-Check 'app.publish-v1-endpoints' (@($published | Where-Object { $_ -ne 200 }).Count -eq 0) ($published -join ',')
Write-Check 'app.new-messages-consumed' (Wait-Counts $target2 $drained) ''
$poisonId = [guid]::NewGuid().ToString('D')
Publish-Sentinel $target2 '' 'urgent_orders' $poisonId '{"Id":"not-a-guid"}' | Out-Null
$poisoned = @{ 'urgent_orders' = 0; 'urgent_orders.dead_letter' = 2 }
Write-Check 'app.poison-message-dead-lettered' (Wait-Counts $target2 $poisoned) ''
$deadLetters = @(Invoke-Management $target2 'POST' '/queues/%2F/urgent_orders.dead_letter/get' @{ count = 10; ackmode = 'ack_requeue_true'; encoding = 'auto' } | ForEach-Object { $_ })
$poisonCopy = @($deadLetters | Where-Object { $_.properties.message_id -eq $poisonId })
Write-Check 'app.poison-copy-has-reason-and-properties' ($poisonCopy.Count -eq 1 -and $poisonCopy[0].properties.headers.'x-dead-letter-reason' -eq 'invalid-payload' -and $poisonCopy[0].properties.delivery_mode -eq 2) ''
$connections = Invoke-Ctl $target2 @('rabbitmqctl', '-q', '-s', 'list_connections', 'client_properties')
Write-Check 'app.publisher-and-consumer-connections' ($connections -match 'template-webapi-publisher' -and $connections -match 'template-webapi-consumers') ''

# Broker restart under a running application: consumers recover automatically, the publisher reconnects lazily.
$since = Now
$restartStarted = Get-Date
& docker restart -t 60 $target2 2>&1 | Out-Null
$brokerBack = Wait-Broker $target2 $since
$recovered = $false
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $recovered -and (Get-Date) -lt $deadline) {
    $recovered = (Publish-Order 'create-order-direct?routingKey=order.urgent') -eq 200
    if (-not $recovered) { Start-Sleep -Milliseconds 500 }
}
$timings['app-publish-recovery-after-broker-restart-seconds'] = [math]::Round(((Get-Date) - $restartStarted).TotalSeconds, 1)
Write-Check 'app.publish-recovers-after-broker-restart' ($brokerBack -and $recovered) "seconds=$($timings['app-publish-recovery-after-broker-restart-seconds'])"
Write-Check 'app.consumers-recover-after-broker-restart' (Wait-Counts $target2 $poisoned 90) ''
Write-Check 'app.topology-after-restart' ((Get-Topology $target2) -ceq $sourceTopology) ''

$stopStarted = Get-Date
Stop-Container "p17-api-tgt-$RunId" 30
$timings['app-shutdown-seconds'] = [math]::Round(((Get-Date) - $stopStarted).TotalSeconds, 1)
$appLog = (& docker logs "p17-api-tgt-$RunId" 2>&1 | Out-String)
Write-Check 'app.graceful-shutdown' ((& docker inspect "p17-api-tgt-$RunId" --format '{{.State.ExitCode}}') -eq '0' -and $appLog -match 'Application stopping') "seconds=$($timings['app-shutdown-seconds'])"
$remaining = Invoke-Ctl $target2 @('rabbitmqctl', '-q', '-s', 'list_connections', 'name')
Write-Check 'app.no-connection-after-shutdown' ([string]::IsNullOrWhiteSpace($remaining)) ''
Write-Check 'app.log-has-no-secret' ($appLog -notmatch [regex]::Escape($env:RABBITMQ_DEFAULT_PASS)) ''
Save '20-app-log-tail.txt' (($appLog -split "`r?`n" | Select-Object -Last 80) -join "`n")

# ---------------------------------------------------------------- 6. Timed switch back to the untouched source
Stop-Container $target2
$rollbackStarted = Get-Date
$since = Now
& docker start $source 2>&1 | Out-Null
$sourceBack = Wait-Broker $source $since
Start-WebApi "p17-api-rb-$RunId" $source $true
$rollbackReady = Wait-WebApiBrokerReady
$rollbackPublish = Publish-Order 'create-order-direct?routingKey=order.urgent'
$timings['rollback-to-source-seconds'] = [math]::Round(((Get-Date) - $rollbackStarted).TotalSeconds, 1)
Write-Check 'rollback.application-on-source' ($sourceBack -and $null -ne $rollbackReady -and $rollbackPublish -eq 200) "seconds=$($timings['rollback-to-source-seconds'])"
Write-Check 'rollback.source-still-4.2.2' ((Invoke-Ctl $source @('rabbitmq-diagnostics', '-q', 'server_version')) -eq '4.2.2') ''
Write-Check 'rollback.source-feature-flags-unchanged' ((Get-FeatureFlags $source) -ceq $sourceFlags) ''
Write-Check 'rollback.source-topology-unchanged' ((Get-Topology $source) -ceq $sourceTopology) ''
$sourceDeadLetter = @(Invoke-Management $source 'POST' '/queues/%2F/urgent_orders.dead_letter/get' @{ count = 10; ackmode = 'ack_requeue_true'; encoding = 'auto' } | ForEach-Object { $_ })
Write-Check 'rollback.source-dead-letter-sentinel-intact' ($sourceDeadLetter.Count -eq 1 -and $sourceDeadLetter[0].properties.message_id -eq $deadLetterId) ''
Write-Check 'rollback.source-messages-consumed-by-app' (Wait-Counts $source $drained) ''
$sourceMounts = @(docker ps -a --filter "volume=$source-data" --format '{{.Names}}')
Write-Check 'rollback.source-volume-only-opened-by-source' ($sourceMounts.Count -eq 1 -and $sourceMounts[0] -eq $source) ($sourceMounts -join ',')
Stop-Container "p17-api-rb-$RunId" 30
Stop-Container $source

# ---------------------------------------------------------------- 7. Retention, secrets and summary
Write-Check 'isolation.normal-volumes-untouched' ((Get-NormalVolumes) -eq $normalVolumesBefore) ''
$leaks = @(Get-ChildItem $outputDirectory -Recurse -File | Where-Object { (Get-Content $_.FullName -Raw) -match [regex]::Escape($env:RABBITMQ_DEFAULT_PASS) })
Write-Check 'secrets.no-password-in-evidence' ($leaks.Count -eq 0) ($leaks.Name -join ',')
$jwtLeaks = @(Get-ChildItem $outputDirectory -Recurse -File | Where-Object { ([string](Get-Content $_.FullName -Raw)).Contains($env:JwtSettings__Key) })
Write-Check 'secrets.no-jwt-key-in-evidence' ($jwtLeaks.Count -eq 0) ($jwtLeaks.Name -join ',')
$hashOutsideSecure = @(Get-ChildItem $outputDirectory -Recurse -File | Where-Object { $_.FullName -notlike "$secureDirectory*" -and (Get-Content $_.FullName -Raw) -match '"password_hash"\s*:' })
Write-Check 'secrets.hashes-only-in-secure-folder' ($hashOutsideSecure.Count -eq 0) ''
$retained = @(docker ps -a --format '{{.Names}}|{{.Status}}' | Where-Object { $_ -like "p17-*-$RunId|*" })
Save '21-retained-containers.txt' ($retained -join "`n")
Save '22-retained-volumes.txt' ((docker volume ls --format '{{.Name}}' | Where-Object { $_ -like "p17-*-$RunId-data" }) -join "`n")
Write-Check 'retention.all-brokers-stopped-not-removed' (@($retained | Where-Object { $_ -match '\|Up ' }).Count -eq 0 -and $retained.Count -ge 9) "containers=$($retained.Count)"

$timings | ConvertTo-Json | Set-Content (Join-Path $outputDirectory 'timings.json')
Remove-Item Env:JwtSettings__Key -ErrorAction SilentlyContinue
$results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $outputDirectory 'results.json')
$failed = @($results | Where-Object { -not $_.Passed })
"run=$RunId checks=$($results.Count) failed=$($failed.Count) rollback-seconds=$($timings['rollback-to-source-seconds'])" | Tee-Object -FilePath (Join-Path $outputDirectory 'summary.txt')
if ($failed.Count -gt 0) { exit 1 }
