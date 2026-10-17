<#
.SYNOPSIS
    Rehearses the Phase 11 Elastic upgrade (8.17.4 -> 8.19.23 -> 9.5.5) in a parallel, disposable environment.
.DESCRIPTION
    Every container, volume and network created here is new and named "p11-<role>-<RunId>"; nothing from the compose
    project (containers elasticsearch/kibana, volume v01) is read, written or removed. Data is synthetic and is written
    by the previous (8.x) WebApi build through its own HTTP endpoints and Serilog sink, so the source holds the real
    8.x document format, sink templates and data stream. The path follows the official guidance:

      source 8.17.4 --snapshot S0--> new 8.19.23 cluster (deprecation checks, Kibana Upgrade Assistant)
                    --snapshot S1--> new 9.5.5 cluster (+ Kibana 9.5.5)

    No 9.x node ever opens 8.x data; data moves only through snapshots in a host directory. Repository-owned
    templates (non-managed, non-dot) are copied explicitly and snapshots are restored without global state, plus the
    Kibana feature state. At the end the 9.x WebApi runs against the target, the target is restarted, and rollback is
    demonstrated by running the 8.x WebApi against the untouched source.

    Containers are stopped (not removed) at the end; volumes, the snapshot directory and the evidence are kept.
    Cleanup is a separate, explicit decision and is only printed, never executed.
.PARAMETER WebApi8xPath
    Folder with the published pre-upgrade WebApi (client 8.17.1, sink 8.12.3).
.PARAMETER WebApi9xPath
    Folder with the published upgraded WebApi (client 9.x, sink 9.x).
#>
param(
    [Parameter(Mandatory = $true)][string]$WebApi8xPath,
    [Parameter(Mandatory = $true)][string]$WebApi9xPath,
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [string]$RunId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not expose $PSScriptRoot to parameter defaults.
if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\validation\elastic-rehearsal\$RunId" }

$images = @{
    Es817  = 'docker.elastic.co/elasticsearch/elasticsearch:8.17.4@sha256:413d8df69823e9f41f6008f025fcbaf31a5849ab9c74894731d7ded65543cd56'
    Kb817  = 'docker.elastic.co/kibana/kibana:8.17.4@sha256:45859e3220fa73a338d59810eb1c5b16e2478ca928654fe994f4fcca9f639b4f'
    Es819  = 'docker.elastic.co/elasticsearch/elasticsearch:8.19.23@sha256:4d0724bb8d78d7a2330693623e26ca4d621079d062ecc8a30b3e1fc180ec14c1'
    Kb819  = 'docker.elastic.co/kibana/kibana:8.19.23@sha256:920b24f689833d34205f426f5359864378a572e829c53016073f0a6837c2e995'
    Es955  = 'docker.elastic.co/elasticsearch/elasticsearch:9.5.5@sha256:13b4a40bb095774f5fda635e45fd5677cc6a26565e42b24997b0c72b88e75984'
    Kb955  = 'docker.elastic.co/kibana/kibana:9.5.5@sha256:13b72ce7222c36e4213229004ae62a87aeb32fd372ebfa6fb38f7a3285fdbb78'
}
$network = "p11-net-$RunId"
$es = @{
    Source = @{ Name = "p11-es-source-817-$RunId"; Port = 19217; Volume = "p11-es-source-817-data-$RunId"; Image = $images.Es817; Cluster = 'p11-source-817' }
    Hop    = @{ Name = "p11-es-hop-819-$RunId";    Port = 19219; Volume = "p11-es-hop-819-data-$RunId";    Image = $images.Es819; Cluster = 'p11-hop-819' }
    Target = @{ Name = "p11-es-target-955-$RunId"; Port = 19255; Volume = "p11-es-target-955-data-$RunId"; Image = $images.Es955; Cluster = 'p11-target-955' }
}
$kb = @{
    Source = @{ Name = "p11-kb-source-817-$RunId"; Port = 15617; Image = $images.Kb817; Es = "p11-es-source-817-$RunId" }
    Hop    = @{ Name = "p11-kb-hop-819-$RunId";    Port = 15619; Image = $images.Kb819; Es = "p11-es-hop-819-$RunId" }
    Target = @{ Name = "p11-kb-target-955-$RunId"; Port = 15655; Image = $images.Kb955; Es = "p11-es-target-955-$RunId" }
}
$snapshotDirectory = Join-Path $OutputDirectory 'snapshots'
$evidence = Join-Path $OutputDirectory 'evidence'
$checks = New-Object System.Collections.ArrayList
$startedAt = Get-Date

New-Item -ItemType Directory -Force -Path $snapshotDirectory, $evidence | Out-Null

function Write-Step([string]$Text) { Write-Host "==> $Text" }

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    [void]$checks.Add([pscustomobject]@{ check = $Name; passed = $Passed; detail = $Detail })
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "   [$mark] $Name $Detail"
}

function Invoke-Http([string]$Method, [string]$Url, [string]$Body = $null, [string]$ContentType = 'application/json', [hashtable]$Headers = @{}, [switch]$AllowError) {
    $parameters = @{ Method = $Method; Uri = $Url; UseBasicParsing = $true; Headers = $Headers; TimeoutSec = 120 }
    if ($Body) { $parameters.Body = [System.Text.Encoding]::UTF8.GetBytes($Body); $parameters.ContentType = "$ContentType; charset=utf-8" }
    try {
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = [string]$response.Content }
    }
    catch [System.Net.WebException] {
        $failed = $_.Exception.Response
        if ($null -eq $failed) { if ($AllowError) { return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message } }; throw }
        $reader = New-Object System.IO.StreamReader($failed.GetResponseStream())
        $result = [pscustomobject]@{ Status = [int]$failed.StatusCode; Content = $reader.ReadToEnd() }
        if ($AllowError) { return $result }
        throw "HTTP $($result.Status) $Method $Url : $($result.Content)"
    }
}

function Es([hashtable]$Node, [string]$Method, [string]$Path, [string]$Body = $null, [string]$ContentType = 'application/json') {
    (Invoke-Http $Method "http://127.0.0.1:$($Node.Port)$Path" $Body $ContentType).Content
}

function EsJson([hashtable]$Node, [string]$Method, [string]$Path, [string]$Body = $null) {
    Es $Node $Method $Path $Body | ConvertFrom-Json
}

function Save-Evidence([string]$Name, [string]$Content) {
    [System.IO.File]::WriteAllText((Join-Path $evidence $Name), $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$What) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try { if (& $Condition) { return } } catch { }
        Start-Sleep -Seconds 2
    }
    throw "Timed out after $TimeoutSeconds s waiting for $What"
}

function Assert-Absent([string]$Kind, [string]$Name) {
    $existing = if ($Kind -eq 'volume') { docker volume ls -q --filter "name=^$Name$" } else { docker ps -aq --filter "name=^$Name$" }
    if ($existing) { throw "$Kind '$Name' already exists. This script never reuses or removes existing resources; choose a clean environment." }
}

function Start-Es([hashtable]$Node) {
    Assert-Absent 'container' $Node.Name
    Assert-Absent 'volume' $Node.Volume
    docker volume create $Node.Volume | Out-Null
    docker run -d --name $Node.Name --network $network --memory 1536m `
        -p "127.0.0.1:$($Node.Port):9200" `
        -v "$($Node.Volume):/usr/share/elasticsearch/data" `
        -v "${snapshotDirectory}:/snapshots" `
        -e "cluster.name=$($Node.Cluster)" -e "node.name=$($Node.Name)" -e 'discovery.type=single-node' `
        -e 'xpack.security.enabled=false' -e 'ingest.geoip.downloader.enabled=false' -e 'path.repo=/snapshots' `
        -e 'ES_JAVA_OPTS=-Xms512m -Xmx512m' $Node.Image | Out-Null
    Wait-Es $Node
}

function Wait-Es([hashtable]$Node) {
    Wait-Until { (Invoke-Http GET "http://127.0.0.1:$($Node.Port)/_cluster/health?wait_for_status=yellow&timeout=5s" -AllowError).Status -eq 200 } 240 "$($Node.Name) yellow"
}

function Start-Kibana([hashtable]$Node) {
    Assert-Absent 'container' $Node.Name
    docker run -d --name $Node.Name --network $network --memory 1536m -p "127.0.0.1:$($Node.Port):5601" `
        -e "ELASTICSEARCH_HOSTS=http://$($Node.Es):9200" -e 'TELEMETRY_OPTIN=false' $Node.Image | Out-Null
    Wait-Until {
        $status = Invoke-Http GET "http://127.0.0.1:$($Node.Port)/api/status" -AllowError
        $status.Status -eq 200 -and ($status.Content | ConvertFrom-Json).status.overall.level -eq 'available'
    } 420 "$($Node.Name) available"
}

# Phase 22: one per-run JWT signing key from a cryptographically secure source, held only in this process and passed to
# each WebApi process environment (it overrides any appsettings value of an older build). Never printed or logged.
$jwtKeyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($jwtKeyBytes)
$jwtSigningKey = [Convert]::ToBase64String($jwtKeyBytes)

function Start-WebApi([string]$Path, [hashtable]$Node, [int]$Port, [string]$LogName) {
    $environment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        JwtSettings__Key = $jwtSigningKey
        ASPNETCORE_URLS = "http://127.0.0.1:$Port"
        UseInMemoryDatabase = 'true'
        RabbitMqSettings__Enabled = 'false'
        PersonSearchSettings__Url = "http://127.0.0.1:$($Node.Port)"
        PersonSearchSettings__IndexName = 'persons'
        ElasticLoggingSettings__Url = "http://127.0.0.1:$($Node.Port)"
        ElasticsearchHealthCheckSettings__Url = "http://127.0.0.1:$($Node.Port)"
    }
    $saved = @{}
    foreach ($key in $environment.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, $environment[$key]) }
    try {
        $process = Start-Process -FilePath $DotNetPath -ArgumentList 'Template.WebApi.dll' -WorkingDirectory $Path -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $evidence "$LogName.out.log") -RedirectStandardError (Join-Path $evidence "$LogName.err.log")
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    Wait-Until { (Invoke-Http GET "http://127.0.0.1:$Port/swagger/v1/swagger.json" -AllowError).Status -eq 200 } 90 "WebApi on $Port"
    return $process
}

function Stop-WebApi($Process) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force; $Process.WaitForExit(10000) | Out-Null }
}

function Get-OwnedTemplates([hashtable]$Node) {
    # Non-system templates not managed by Elasticsearch itself: what an operator must carry to a new cluster.
    $component = (EsJson $Node GET '/_component_template').component_templates |
        Where-Object { -not $_.name.StartsWith('.') -and -not ($_.component_template._meta -and $_.component_template._meta.managed) }
    $index = (EsJson $Node GET '/_index_template').index_templates |
        Where-Object { -not $_.name.StartsWith('.') -and -not ($_.index_template._meta -and $_.index_template._meta.managed) }
    [pscustomobject]@{ Component = @($component); Index = @($index) }
}

function Copy-OwnedTemplates([hashtable]$From, [hashtable]$To) {
    $owned = Get-OwnedTemplates $From
    foreach ($template in $owned.Component) { Es $To PUT "/_component_template/$($template.name)" ($template.component_template | ConvertTo-Json -Depth 100 -Compress) | Out-Null }
    foreach ($template in $owned.Index) { Es $To PUT "/_index_template/$($template.name)" ($template.index_template | ConvertTo-Json -Depth 100 -Compress) | Out-Null }
    return $owned
}

function Get-View([hashtable]$Node) {
    Es $Node POST '/_refresh' | Out-Null
    $sources = foreach ($id in 1, 2, 5, 7, 10, 15, 20, 25, 30) { "$id=" + (Es $Node GET "/persons/_source/$id") }
    $match = EsJson $Node POST '/persons/_search' '{"size":100,"query":{"match":{"firstName":"Ana"}},"_source":false,"sort":[{"id":"asc"}]}'
    $term = EsJson $Node POST '/persons/_search' '{"size":100,"query":{"term":{"lastName.keyword":"Souza"}},"_source":false,"sort":[{"id":"asc"}]}'
    [pscustomobject]@{
        persons = (EsJson $Node GET '/persons/_count').count
        alias = (EsJson $Node GET '/persons-read/_count').count
        logs = (EsJson $Node GET '/logs-api-example-demo/_count').count
        sources = ($sources -join ' | ')
        matchIds = (($match.hits.hits | ForEach-Object { $_._id }) -join ',')
        termIds = (($term.hits.hits | ForEach-Object { $_._id }) -join ',')
        mapping = ((EsJson $Node GET '/persons/_mapping').persons.mappings | ConvertTo-Json -Depth 100 -Compress)
    }
}

function Register-Repository([hashtable]$Node, [string]$Repository, [bool]$Writable) {
    # One writer per repository (its owner); every other cluster registers it read-only, as Elastic requires.
    $readonly = if ($Writable) { 'false' } else { 'true' }
    Es $Node PUT "/_snapshot/$Repository" "{`"type`":`"fs`",`"settings`":{`"location`":`"/snapshots/$Repository`",`"readonly`":$readonly}}" | Out-Null
}

function New-Snapshot([hashtable]$Node, [string]$Repository, [string]$Name) {
    Register-Repository $Node $Repository $true
    $body = '{"indices":"persons,logs-api-example-demo","include_global_state":false,"feature_states":["kibana"],"metadata":{"purpose":"phase-11 rehearsal, synthetic data"}}'
    $result = EsJson $Node PUT "/_snapshot/$Repository/${Name}?wait_for_completion=true" $body
    Save-Evidence "snapshot-$Name.json" ($result | ConvertTo-Json -Depth 20)
    return $result.snapshot
}

function Restore-Snapshot([hashtable]$Node, [string]$Repository, [string]$Name) {
    Register-Repository $Node $Repository $false
    $body = '{"indices":"persons,logs-api-example-demo","include_global_state":false,"include_aliases":true,"feature_states":["kibana"]}'
    $result = EsJson $Node POST "/_snapshot/$Repository/$Name/_restore?wait_for_completion=true" $body
    Save-Evidence "restore-$Name-into-$($Node.Name).json" ($result | ConvertTo-Json -Depth 20)
    Wait-Es $Node
    return $result.snapshot
}

$processes = @()
try {
    Write-Step 'Preflight'
    docker version --format '{{.Server.Version}}' | Out-Null
    if (-not (Test-Path (Join-Path $WebApi8xPath 'Template.WebApi.dll'))) { throw "8.x WebApi not found in $WebApi8xPath" }
    if (-not (Test-Path (Join-Path $WebApi9xPath 'Template.WebApi.dll'))) { throw "9.x WebApi not found in $WebApi9xPath" }
    if (-not (docker network ls -q --filter "name=^$network$")) { docker network create $network | Out-Null }
    Save-Evidence 'images.json' ($images | ConvertTo-Json)
    # Pull the digest-pinned images first: with ErrorActionPreference=Stop, the "Unable to find image locally" line that
    # docker run writes to stderr on a cache miss would otherwise abort the run (Phase 21, after a host image prune).
    foreach ($image in $images.Values) {
        $ErrorActionPreference = 'Continue'
        docker pull -q $image 2>&1 | Out-Null
        $pulled = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($pulled -ne 0) { throw "docker pull failed for $image" }
    }

    # 1. Source 8.17.4 (current compose version) with Kibana and data written by the 8.x WebApi.
    Write-Step 'Source 8.17.4 cluster and Kibana'
    Start-Es $es.Source
    Start-Kibana $kb.Source
    $dataView = Invoke-Http POST "http://127.0.0.1:$($kb.Source.Port)/api/data_views/data_view" '{"data_view":{"title":"logs-api-example-*","name":"phase11 logs"}}' -Headers @{ 'kbn-xsrf' = 'true' }
    $dataViewId = ($dataView.Content | ConvertFrom-Json).data_view.id
    Add-Check 'kibana 8.17.4 data view created' ([bool]$dataViewId) $dataViewId
    docker stop $kb.Source.Name | Out-Null

    Write-Step 'Seed through the 8.x WebApi (client 8.17.1, sink 8.12.3)'
    $api8 = Start-WebApi $WebApi8xPath $es.Source 5281 'webapi-8x-seed'
    $processes += $api8
    $base8 = 'http://127.0.0.1:5281/api/v1/Person'
    Invoke-Http POST "$base8/create-index?indexName=persons" | Out-Null
    foreach ($id in 1..30) {
        $first = if ($id % 5 -eq 0) { 'Ana' } else { "Pessoa$id" }
        $last = if ($id % 3 -eq 0) { $null } elseif ($id % 2 -eq 0) { 'Souza' } else { 'Lima' }
        $body = @{ id = $id; firstName = $first; lastName = $last } | ConvertTo-Json -Compress
        Invoke-Http POST "$base8/add-person" $body | Out-Null
    }
    Invoke-Http POST "$base8/update-person" '{"id":2,"firstName":"Bia","lastName":"Souza"}' | Out-Null
    Invoke-Http POST "$base8/add-person" '{"id":31,"firstName":"Jo\u00e3o","lastName":"Concei\u00e7\u00e3o"}' | Out-Null
    Add-Check '8.x get-person' ((Invoke-Http GET "$base8/get-person/31").Content -match 'Concei(\\u00E7|\u00E7)')
    Wait-Until { ((Invoke-Http GET "http://127.0.0.1:$($es.Source.Port)/logs-api-example-demo/_count" -AllowError).Content | ConvertFrom-Json).count -gt 0 } 60 '8.x log events'
    Stop-WebApi $api8
    Es $es.Source POST '/_aliases' '{"actions":[{"add":{"index":"persons","alias":"persons-read"}}]}' | Out-Null

    # Inventory of the source (no credentials or documents).
    foreach ($item in @(
            @('inventory-indices.txt', '/_cat/indices?v&expand_wildcards=all&h=index,health,status,docs.count,pri,rep'),
            @('inventory-aliases.json', '/_alias'),
            @('inventory-index-templates.json', '/_index_template'),
            @('inventory-component-templates.json', '/_component_template'),
            @('inventory-data-streams.json', '/_data_stream'),
            @('inventory-ingest-pipelines.json', '/_ingest/pipeline'),
            @('inventory-ilm-policies.json', '/_ilm/policy'),
            @('inventory-persons-mapping.json', '/persons/_mapping'),
            @('inventory-persons-settings.json', '/persons/_settings'),
            @('inventory-legacy-templates.json', '/_template'))) {
        Save-Evidence $item[0] (Es $es.Source GET $item[1])
    }
    $sourceView = Get-View $es.Source
    Save-Evidence 'view-source-before.json' ($sourceView | ConvertTo-Json)
    $sourceOwned = Get-OwnedTemplates $es.Source
    Add-Check 'source 8.x sink templates present' (($sourceOwned.Index | ForEach-Object name) -contains 'logs-api-example-8.11.0') (($sourceOwned.Index | ForEach-Object name) -join ',')

    # 2. Recovery point S0 on the source.
    Write-Step 'Snapshot S0 of the 8.17.4 source'
    $s0 = New-Snapshot $es.Source 'p11-repo-source' 'p11-s0-source-817'
    Add-Check 'S0 SUCCESS' ($s0.state -eq 'SUCCESS' -and $s0.shards.failed -eq 0) "indices=$($s0.indices -join ',') data_streams=$($s0.data_streams -join ',') features=$(($s0.feature_states | ForEach-Object feature_name) -join ',')"

    # 3. Mandatory hop: a new 8.19.23 cluster restored from S0, where the upgrade checks run.
    Write-Step 'Hop 8.19.23 (new cluster, restore S0)'
    Start-Es $es.Hop
    $hopOwned = Copy-OwnedTemplates $es.Source $es.Hop
    $r0 = Restore-Snapshot $es.Hop 'p11-repo-source' 'p11-s0-source-817'
    Add-Check 'S0 restored on 8.19.23' ($r0.shards.failed -eq 0)
    Start-Kibana $kb.Hop
    $hopView = Get-View $es.Hop
    Add-Check '8.19 parity with source' (($hopView | ConvertTo-Json) -eq ($sourceView | ConvertTo-Json))
    $deprecations = Es $es.Hop GET '/_migration/deprecations'
    Save-Evidence 'deprecations-es-8.19.23.json' $deprecations
    $critical = ([regex]::Matches($deprecations, '"level"\s*:\s*"critical"')).Count
    Add-Check 'no critical Elasticsearch deprecation' ($critical -eq 0) "critical=$critical"
    $features = Es $es.Hop GET '/_migration/system_features'
    Save-Evidence 'system-features-8.19.23.json' $features
    Add-Check 'system features need no migration' (($features | ConvertFrom-Json).migration_status -eq 'NO_MIGRATION_NEEDED') (($features | ConvertFrom-Json).migration_status)
    $assistant = Invoke-Http GET "http://127.0.0.1:$($kb.Hop.Port)/api/upgrade_assistant/status?targetVersion=9.5.5" -AllowError
    if ($assistant.Status -ne 200) { $assistant = Invoke-Http GET "http://127.0.0.1:$($kb.Hop.Port)/api/upgrade_assistant/status" -AllowError }
    Save-Evidence 'upgrade-assistant-status-8.19.23.json' $assistant.Content
    $assistantJson = $assistant.Content | ConvertFrom-Json
    Add-Check 'Kibana Upgrade Assistant ready' ($assistant.Status -eq 200 -and $assistantJson.readyForUpgrade -eq $true) ($assistantJson.details)
    $kibanaDeprecations = Invoke-Http GET "http://127.0.0.1:$($kb.Hop.Port)/api/deprecations/" -AllowError
    Save-Evidence 'deprecations-kibana-8.19.23.json' $kibanaDeprecations.Content
    $kibanaCritical = @((($kibanaDeprecations.Content | ConvertFrom-Json).deprecations) | Where-Object { $_.level -eq 'critical' })
    Add-Check 'no critical Kibana deprecation' ($kibanaCritical.Count -eq 0) (($kibanaCritical | ForEach-Object title) -join '; ')
    docker stop $kb.Hop.Name | Out-Null

    Write-Step 'Snapshot S1 of the 8.19.23 hop'
    $s1 = New-Snapshot $es.Hop 'p11-repo-hop' 'p11-s1-hop-819'
    Add-Check 'S1 SUCCESS' ($s1.state -eq 'SUCCESS' -and $s1.shards.failed -eq 0)

    # 4. Target 9.5.5: new cluster, templates first, then restore S1.
    Write-Step 'Target 9.5.5 (new cluster, restore S1) and Kibana 9.5.5'
    Start-Es $es.Target
    $targetOwned = Copy-OwnedTemplates $es.Hop $es.Target
    $r1 = Restore-Snapshot $es.Target 'p11-repo-hop' 'p11-s1-hop-819'
    Add-Check 'S1 restored on 9.5.5' ($r1.shards.failed -eq 0)
    $targetVersion = (EsJson $es.Target GET '/').version.number
    Add-Check 'target version' ($targetVersion -eq '9.5.5') $targetVersion
    Add-Check 'target cluster health' ((EsJson $es.Target GET '/_cluster/health').status -in @('green', 'yellow'))
    $targetView = Get-View $es.Target
    Save-Evidence 'view-target-after-restore.json' ($targetView | ConvertTo-Json)
    foreach ($property in 'persons', 'alias', 'logs', 'sources', 'matchIds', 'termIds', 'mapping') {
        $value = [string]$sourceView.$property
        $shown = if ($value.Length -gt 60) { "$($value.Length) chars" } else { $value }
        Add-Check "parity $property" ([string]$targetView.$property -eq $value) "source=$shown"
    }
    $templateNames = ($targetOwned.Index | ForEach-Object name) -join ','
    Add-Check 'owned templates migrated' (((Get-OwnedTemplates $es.Target).Index | ForEach-Object name) -join ',' -eq $templateNames) $templateNames
    $stream = (EsJson $es.Target GET '/_data_stream/logs-api-example-demo').data_streams[0]
    Add-Check 'data stream restored' ($stream.name -eq 'logs-api-example-demo') "template=$($stream.template) status=$($stream.status)"
    Start-Kibana $kb.Target
    $migratedView = Invoke-Http GET "http://127.0.0.1:$($kb.Target.Port)/api/data_views/data_view/$dataViewId" -AllowError
    Add-Check 'Kibana 9.5.5 migrated the 8.17.4 data view' ($migratedView.Status -eq 200) "status=$($migratedView.Status)"

    # 5. The upgraded WebApi on the target.
    Write-Step '9.x WebApi against the 9.5.5 target'
    $api9 = Start-WebApi $WebApi9xPath $es.Target 5291 'webapi-9x-target'
    $processes += $api9
    $base9 = 'http://127.0.0.1:5291/api/v1/Person'
    Add-Check '9.x get migrated person' ((Invoke-Http GET "$base9/get-person/31").Content -match 'Concei(\\u00E7|\u00E7)')
    Add-Check '9.x get-all' ((Invoke-Http GET "$base9/get-all-persons").Status -eq 200)
    Add-Check '9.x create-index idempotent' ((Invoke-Http POST "$base9/create-index?indexName=persons").Status -eq 200)
    Add-Check '9.x add' ((Invoke-Http POST "$base9/add-person" '{"id":40,"firstName":"Nova","lastName":"Vers\u00e3o"}').Status -eq 200)
    Add-Check '9.x update' ((Invoke-Http POST "$base9/update-person" '{"id":1,"firstName":"Atualizada"}').Status -eq 200)
    Add-Check '9.x delete' ((Invoke-Http DELETE "$base9/delete-person/3").Status -eq 200)
    Add-Check '9.x missing is 404' ((Invoke-Http GET "$base9/get-person/3" -AllowError).Status -eq 404)
    Invoke-Http POST "$base9/add-person" '{"id":131,"firstName":"Jo\u00e3o","lastName":"Concei\u00e7\u00e3o"}' | Out-Null
    $written8x = (Es $es.Source GET '/persons/_source/31') -replace '"id":31', '"id":131'
    $written9x = Es $es.Target GET '/persons/_source/131'
    Save-Evidence 'stored-format-8x-vs-9x.txt' "8.x: $written8x`n9.x: $written9x"
    Add-Check '9.x stored format byte-identical to 8.x (non-ASCII)' ($written9x -eq $written8x) $written9x
    Add-Check '9.x stored format byte-identical to 8.x (ASCII)' ((Es $es.Target GET '/persons/_source/30') -eq (Es $es.Source GET '/persons/_source/30'))
    $health = Invoke-Http GET 'http://127.0.0.1:5291/health' -AllowError
    Add-Check '9.x /health healthy' ($health.Status -eq 200) $health.Content
    Wait-Until { ((Es $es.Target GET '/_data_stream/logs-api-example-demo') | ConvertFrom-Json).data_streams[0].template -eq 'logs-api-example-9.0.0' } 60 '9.x sink templates'
    $logsBefore = $targetView.logs
    Wait-Until { (EsJson $es.Target POST '/logs-api-example-demo/_refresh' | Out-Null); (EsJson $es.Target GET '/logs-api-example-demo/_count').count -gt $logsBefore } 60 '9.x log events'
    Add-Check '9.x sink writes to the migrated data stream' $true
    $rollover = EsJson $es.Target POST '/logs-api-example-demo/_rollover' '{}'
    Add-Check 'rollover uses the 9.0.0 template' ($rollover.rolled_over -eq $true) $rollover.new_index
    Stop-WebApi $api9
    Save-Evidence 'target-templates-after.json' (Es $es.Target GET '/_index_template/logs-api-example-*')

    # 6. Restart persistence.
    Write-Step 'Restart the target'
    $beforeRestart = Get-View $es.Target
    docker restart $es.Target.Name | Out-Null
    Wait-Es $es.Target
    $afterRestart = Get-View $es.Target
    Save-Evidence 'view-target-before-restart.json' ($beforeRestart | ConvertTo-Json)
    Save-Evidence 'view-target-after-restart.json' ($afterRestart | ConvertTo-Json)
    foreach ($property in 'persons', 'alias', 'logs', 'sources', 'matchIds', 'termIds', 'mapping') {
        Add-Check "restart persistence $property" ([string]$afterRestart.$property -eq [string]$beforeRestart.$property) "before=$(([string]$beforeRestart.$property).Length) chars after=$(([string]$afterRestart.$property).Length) chars"
    }

    # 7. Rollback: the 8.x WebApi against the untouched 8.17.4 source.
    Write-Step 'Rollback: 8.x WebApi against the untouched source'
    if (-not (docker ps -q --filter "name=^$($es.Hop.Name)$")) { docker start $es.Hop.Name | Out-Null; Wait-Es $es.Hop }
    $sourceAfter = Get-View $es.Source
    Add-Check 'source data untouched' (($sourceAfter | ConvertTo-Json) -eq ($sourceView | ConvertTo-Json))
    Add-Check 'source still 8.17.4' ((EsJson $es.Source GET '/').version.number -eq '8.17.4')
    $s0Kept = (EsJson $es.Source GET '/_snapshot/p11-repo-source/p11-s0-source-817').snapshots[0]
    Add-Check 'source snapshot S0 kept' ($s0Kept.state -eq 'SUCCESS') "$($s0Kept.snapshot) $($s0Kept.state)"
    $s1Kept = (EsJson $es.Hop GET '/_snapshot/p11-repo-hop/p11-s1-hop-819').snapshots[0]
    Add-Check 'hop snapshot S1 kept' ($s1Kept.state -eq 'SUCCESS') "$($s1Kept.snapshot) $($s1Kept.state)"
    $rollback = Start-WebApi $WebApi8xPath $es.Source 5282 'webapi-8x-rollback'
    $processes += $rollback
    Add-Check 'rollback get-person (original data)' ((Invoke-Http GET 'http://127.0.0.1:5282/api/v1/Person/get-person/1').Content -match 'Pessoa1')
    Add-Check 'rollback /health healthy' ((Invoke-Http GET 'http://127.0.0.1:5282/health' -AllowError).Status -eq 200)
    Stop-WebApi $rollback
}
catch {
    Add-Check 'rehearsal completed without error' $false ($_ | Out-String)
}
finally {
    foreach ($process in $processes) { Stop-WebApi $process }
    foreach ($name in $kb.Target.Name, $kb.Hop.Name, $kb.Source.Name, $es.Target.Name, $es.Hop.Name, $es.Source.Name) {
        if (docker ps -q --filter "name=^$name$") { docker stop $name | Out-Null }
    }
    $summary = [pscustomobject]@{
        startedAt = $startedAt.ToString('o'); finishedAt = (Get-Date).ToString('o'); images = $images
        passed = @($checks | Where-Object passed).Count; failed = @($checks | Where-Object { -not $_.passed }).Count; checks = $checks
        kept = 'Containers stopped, not removed; volumes, snapshots and evidence kept. Cleanup requires explicit authorization.'
    }
    Save-Evidence 'rehearsal-summary.json' ($summary | ConvertTo-Json -Depth 6)
    Write-Host "Checks: $($summary.passed) passed, $($summary.failed) failed. Evidence: $evidence"
    $containers = (@($kb.Values) + @($es.Values) | ForEach-Object { $_.Name }) -join ' '
    $volumes = (@($es.Values) | ForEach-Object { $_.Volume }) -join ' '
    Write-Host "Cleanup (only with explicit authorization): docker rm $containers; docker volume rm $volumes; docker network rm $network"
    if ($summary.failed -gt 0) { exit 1 }
}
