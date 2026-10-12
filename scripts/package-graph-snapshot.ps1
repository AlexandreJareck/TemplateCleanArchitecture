<#
.SYNOPSIS
    Snapshots the resolved NuGet package graphs, separated into production and test projects.
.DESCRIPTION
    Runs `dotnet list package --include-transitive --format json` per project of the application solution
    filter (projects must be restored) and writes a normalized, sorted text graph per side:
    artifacts/validation/package-graph/<Label>/production.txt and tests.txt (git-ignored), one line per
    "<project>|<framework>|<direct|transitive>|<package>|<resolved version>".
    With -CompareWith, the production graph must be byte-identical to the earlier label (exit 1 otherwise);
    the test-graph differences are printed for review.
#>
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [string]$CompareWith
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
$root = Join-Path $repositoryRoot 'artifacts\validation\package-graph'
$output = Join-Path $root $Label
New-Item -ItemType Directory -Force -Path $output | Out-Null

$filter = Get-Content -Raw 'TemplateCleanArchitecture.App.slnf' | ConvertFrom-Json
$production = New-Object System.Collections.Generic.List[string]
$tests = New-Object System.Collections.Generic.List[string]
foreach ($project in $filter.solution.projects) {
    $json = (& $DotNetPath list $project package --include-transitive --format json) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed for $project" }
    $graph = $json | ConvertFrom-Json
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    # Assign without the pipeline so an empty list is not unrolled to `$null.
    $target = $production
    if ($project -like 'Tests\*') { $target = $tests }
    foreach ($graphProject in $graph.projects) {
        foreach ($framework in @($graphProject.frameworks)) {
            if (-not $framework) { continue }
            foreach ($package in @($framework.topLevelPackages)) {
                if ($package) { $target.Add("$name|$($framework.framework)|direct|$($package.id)|$($package.resolvedVersion)") }
            }
            foreach ($package in @($framework.transitivePackages)) {
                if ($package) { $target.Add("$name|$($framework.framework)|transitive|$($package.id)|$($package.resolvedVersion)") }
            }
        }
    }
}
$production | Sort-Object | Set-Content (Join-Path $output 'production.txt')
$tests | Sort-Object | Set-Content (Join-Path $output 'tests.txt')
Write-Host "Production entries: $($production.Count); test entries: $($tests.Count) -> $output"

if ($CompareWith) {
    $baseline = Join-Path $root $CompareWith
    $productionDifference = @(Compare-Object (Get-Content (Join-Path $baseline 'production.txt')) (Get-Content (Join-Path $output 'production.txt')))
    $testDifference = @(Compare-Object (Get-Content (Join-Path $baseline 'tests.txt')) (Get-Content (Join-Path $output 'tests.txt')))
    $report = @("Production graph differences: $($productionDifference.Count)") +
        @($productionDifference | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" }) +
        @("Test graph differences: $($testDifference.Count)") +
        @($testDifference | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" })
    $report | Set-Content (Join-Path $output "compare-$CompareWith.txt")
    $report | Out-Host
    if ($productionDifference.Count -ne 0) { Write-Host 'Production package graph CHANGED'; exit 1 }
    Write-Host 'Production package graph unchanged'
}
exit 0
