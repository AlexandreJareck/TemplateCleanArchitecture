<#
.SYNOPSIS
    Captures reproducible per-project test discovery and execution evidence (and optional coverage).
.DESCRIPTION
    For each test project, sequentially:
      1. lists the discovered tests with `dotnet test --list-tests` (VSTest adapter discovery),
      2. executes the project with a TRX logger, optionally collecting coverage with the coverlet
         "XPlat Code Coverage" data collector (Cobertura) for the projects that reference it,
      3. parses the TRX counters (total, executed, passed, failed, not executed).
    Optionally, discovery is repeated with an external vstest.console.exe (for example the one shipped with
    Visual Studio) to exercise the IDE test platform host. This is not Visual Studio Test Explorer.

    Results go to artifacts/validation/test-tooling/<Label>/ (git-ignored): one discovery list per project,
    TRX files, coverage files and summary.json. Use -CompareWith to diff the discovery lists and counts
    against an earlier label; the script exits non-zero on any count or name difference or failed test.
    Projects must already be built (the script uses --no-build).
#>
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [string[]]$Projects = @(
        'Tests\Template.Architecture.Tests\Template.Architecture.Tests.csproj',
        'Tests\Template.Unit.Tests\Template.Unit.Tests.csproj',
        'Tests\Template.Integration.Tests\Template.Integration.Tests.csproj',
        'Tests\Template.PostgreSql.Integration.Tests\Template.PostgreSql.Integration.Tests.csproj',
        'Tests\Template.RabbitMq.Integration.Tests\Template.RabbitMq.Integration.Tests.csproj',
        'Tests\Template.Elasticsearch.Integration.Tests\Template.Elasticsearch.Integration.Tests.csproj',
        'Tests\Template.Redis.Integration.Tests\Template.Redis.Integration.Tests.csproj',
        'Tests\Template.Functional.Tests\Template.Functional.Tests.csproj'),
    # Collect coverage for the projects that reference coverlet.collector.
    [switch]$Coverage,
    # Only list tests; do not execute them.
    [switch]$DiscoveryOnly,
    # Optional vstest.console.exe used for a second, IDE-host discovery pass.
    [string]$VsTestConsolePath,
    [string]$CompareWith
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
# English, machine-independent CLI output so the discovery list can be parsed; restored before exiting.
$previousLanguage = @($env:DOTNET_CLI_UI_LANGUAGE, $env:VSLANG)
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:VSLANG = '1033'
$root = Join-Path $repositoryRoot 'artifacts\validation\test-tooling'
$output = Join-Path $root $Label
if (Test-Path $output) {
    # Only this script's own generated evidence for the same label is replaced.
    Remove-Item -Recurse -Force $output
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Get-ProjectName([string]$Path) { [IO.Path]::GetFileNameWithoutExtension($Path) }

function Get-ListedTests([string[]]$Lines) {
    $started = $false
    $tests = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        if ($line -match 'The following Tests are available:|Os Testes a seguir est') { $started = $true; continue }
        if ($started -and $line -match '^\s{4}(\S.*)$') { $tests.Add($Matches[1].TrimEnd()) }
    }
    return , $tests.ToArray()
}

$results = @()
$failed = $false
foreach ($project in $Projects) {
    $name = Get-ProjectName $project
    $projectOutput = Join-Path $output $name
    New-Item -ItemType Directory -Force -Path $projectOutput | Out-Null
    Write-Host "==> $name"

    $listLines = & $DotNetPath test $project --no-build --list-tests --verbosity minimal 2>&1 | ForEach-Object { "$_" }
    $listExit = $LASTEXITCODE
    $listLines | Set-Content (Join-Path $projectOutput 'list-tests.log')
    $listed = Get-ListedTests $listLines
    $listed | Sort-Object | Set-Content (Join-Path $projectOutput 'discovered.txt')

    $assembly = $null
    $vsListed = $null
    if ($VsTestConsolePath) {
        $assembly = Join-Path (Split-Path -Parent (Join-Path $repositoryRoot $project)) "bin\Debug\net10.0\$name.dll"
        $vsLines = & $VsTestConsolePath $assembly '/ListTests' 2>&1 | ForEach-Object { "$_" }
        $vsLines | Set-Content (Join-Path $projectOutput 'vstest-console-list.log')
        $vsListed = Get-ListedTests $vsLines
        $vsListed | Sort-Object | Set-Content (Join-Path $projectOutput 'vstest-console-discovered.txt')
    }

    # The adapter package copies its assemblies with PreserveNewest, so after a downgrade an incremental build can keep
    # the newer copy; record what the test host will actually load.
    $adapter = Join-Path (Split-Path -Parent (Join-Path $repositoryRoot $project)) 'bin\Debug\net10.0\xunit.runner.visualstudio.testadapter.dll'
    $entry = [ordered]@{
        project = $project.Replace('\', '/')
        adapterVersion = if (Test-Path $adapter) { (Get-Item $adapter).VersionInfo.ProductVersion } else { $null }
        listExitCode = $listExit
        discovered = $listed.Count
        vsTestConsoleDiscovered = if ($null -ne $vsListed) { $vsListed.Count } else { $null }
        testExitCode = $null
        total = $null; executed = $null; passed = $null; failed = $null; notExecuted = $null
        coverageFiles = @()
        durationSeconds = $null
    }
    if ($listExit -ne 0 -or $listed.Count -eq 0) { $failed = $true }

    if (-not $DiscoveryOnly) {
        $resultsDirectory = Join-Path $projectOutput 'results'
        $arguments = @('test', $project, '--no-build', '--verbosity', 'minimal',
            '--logger', 'trx;LogFileName=results.trx', '--results-directory', $resultsDirectory)
        $hasCollector = Select-String -Path $project -Pattern 'coverlet.collector' -Quiet
        if ($Coverage -and $hasCollector) { $arguments += @('--collect', 'XPlat Code Coverage') }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        & $DotNetPath @arguments 2>&1 | ForEach-Object { "$_" } | Set-Content (Join-Path $projectOutput 'test.log')
        $entry.testExitCode = $LASTEXITCODE
        $entry.durationSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
        $trx = Join-Path $resultsDirectory 'results.trx'
        if (Test-Path $trx) {
            [xml]$document = Get-Content -Raw $trx
            $counters = $document.TestRun.ResultSummary.Counters
            $entry.total = [int]$counters.total
            $entry.executed = [int]$counters.executed
            $entry.passed = [int]$counters.passed
            $entry.failed = [int]$counters.failed
            $entry.notExecuted = [int]$counters.notExecuted
        }
        # The TRX logger also copies each attachment below <user>_<machine>_<time>\In\; keep only the collector output.
        $entry.coverageFiles = @(Get-ChildItem -Path $resultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\In\\' } | ForEach-Object { $_.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/') })
        if ($entry.testExitCode -ne 0 -or $entry.failed -ne 0 -or $entry.total -ne $entry.discovered) { $failed = $true }
    }
    $results += New-Object PSObject -Property $entry
    Write-Host ("    adapter={6} discovered={0} total={1} passed={2} failed={3} notExecuted={4} exit={5}" -f $entry.discovered, $entry.total, $entry.passed, $entry.failed, $entry.notExecuted, $entry.testExitCode, $entry.adapterVersion)
}

$summary = [ordered]@{
    label = $Label
    createdAt = (Get-Date -Format o)
    sdk = (& $DotNetPath --version)
    projects = $results
}
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'summary.json')

if ($CompareWith) {
    $baselineDirectory = Join-Path $root $CompareWith
    $baseline = Get-Content -Raw (Join-Path $baselineDirectory 'summary.json') | ConvertFrom-Json
    $report = New-Object System.Collections.Generic.List[string]
    foreach ($current in $results) {
        $name = Get-ProjectName $current.project
        $previous = $baseline.projects | Where-Object { (Get-ProjectName $_.project) -eq $name }
        if (-not $previous) { $report.Add("${name}: missing from $CompareWith"); $failed = $true; continue }
        $before = @(Get-Content (Join-Path (Join-Path $baselineDirectory $name) 'discovered.txt'))
        $after = @(Get-Content (Join-Path (Join-Path $output $name) 'discovered.txt'))
        $difference = @(Compare-Object $before $after)
        $line = "{0}: discovered {1} -> {2}; total {3} -> {4}; passed {5} -> {6}; name differences {7}" -f $name,
            $previous.discovered, $current.discovered, $previous.total, $current.total, $previous.passed, $current.passed, $difference.Count
        $report.Add($line)
        if ($difference.Count -ne 0 -or $previous.discovered -ne $current.discovered -or
            ((-not $DiscoveryOnly) -and ($previous.total -ne $current.total -or $previous.passed -ne $current.passed))) {
            $failed = $true
            $difference | ForEach-Object { $report.Add("    $($_.SideIndicator) $($_.InputObject)") }
        }
    }
    $report | Set-Content (Join-Path $output "compare-$CompareWith.txt")
    $report | Out-Host
}

$env:DOTNET_CLI_UI_LANGUAGE = $previousLanguage[0]
$env:VSLANG = $previousLanguage[1]
if ($failed) { Write-Host 'Test tooling evidence: FAILED'; exit 1 }
Write-Host 'Test tooling evidence: OK'
exit 0
