<#
.SYNOPSIS
    Consumes coverlet Cobertura reports, validates them, merges them per module and enforces per-layer thresholds.
.DESCRIPTION
    Reads every coverage.cobertura.xml below -InputDirectory (for example the output of
    scripts/test-tooling-evidence.ps1 -Coverage), and:
      1. validates the structure of each report (root counters, sources, at least one package/class/line);
      2. resolves every class file to a repository-relative path (independent of how the collector
         splits <source> roots);
      3. merges the reports per module (assembly): a line is covered when any report hit it, a branch
         line keeps the best covered/total pair, a method is covered when any of its lines was hit;
      4. applies the documented exclusions (generated code only) from the thresholds file;
      5. writes coverage-summary.json and coverage-summary.md next to the input and enforces, per module,
         the line/branch minimums, the presence of every required module and its minimum source count.
    Exits 1 when a report is invalid, a required module or source set is missing, or a threshold is missed.
    No module is averaged with another.
#>
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [string]$ThresholdsPath,
    # Only validate and summarize; do not enforce thresholds.
    [switch]$NoEnforce
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ThresholdsPath) { $ThresholdsPath = Join-Path $repositoryRoot 'Tests\test-coverage-policy.json' }
$InputDirectory = (Resolve-Path $InputDirectory).Path
$policy = Get-Content -Raw $ThresholdsPath | ConvertFrom-Json
$exclusions = @($policy.exclusions | ForEach-Object { $_.pattern })
$problems = New-Object System.Collections.Generic.List[string]

function Get-RelativePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $full = $full.Substring($repositoryRoot.Length).TrimStart('\', '/')
    }
    return $full.Replace('\', '/')
}

function Test-Excluded([string]$RelativePath) {
    foreach ($pattern in $exclusions) { if ($RelativePath -like $pattern) { return $true } }
    return $false
}

# The TRX logger copies each attachment below <user>_<machine>_<time>\In\; those duplicates are skipped.
$reports = @(Get-ChildItem -Path $InputDirectory -Recurse -Filter 'coverage.cobertura.xml' | Where-Object { $_.FullName -notmatch '[\\/]In[\\/]' })
if ($reports.Count -eq 0) { Write-Host "No coverage.cobertura.xml below $InputDirectory"; exit 1 }

# module -> @{ lines = file|line -> hits; branches = file|line -> @(covered,total); methods = key -> covered; files = set }
$modules = @{}
$reportInfo = @()
foreach ($report in $reports) {
    [xml]$document = Get-Content -Raw $report.FullName
    $coverage = $document.coverage
    $relativeReport = Get-RelativePath $report.FullName
    if (-not $coverage -or $null -eq $coverage.'line-rate' -or $null -eq $coverage.'lines-valid') {
        $problems.Add("$relativeReport is not a Cobertura report with root counters"); continue
    }
    $sources = @($coverage.sources.source | Where-Object { $_ })
    $packages = @($coverage.packages.package | Where-Object { $_ })
    if ($sources.Count -eq 0) { $problems.Add("$relativeReport has no <source>") }
    if ($packages.Count -eq 0 -or [int]$coverage.'lines-valid' -eq 0) { $problems.Add("$relativeReport has no packages or lines") }
    $reportInfo += [ordered]@{
        report = $relativeReport; version = $coverage.version; sources = $sources.Count
        packages = @($packages | ForEach-Object { $_.name })
        linesValid = [int]$coverage.'lines-valid'; linesCovered = [int]$coverage.'lines-covered'
        branchesValid = [int]$coverage.'branches-valid'; branchesCovered = [int]$coverage.'branches-covered'
    }
    foreach ($package in $packages) {
        if (-not $modules.ContainsKey($package.name)) {
            $modules[$package.name] = @{ lines = @{}; branches = @{}; methods = @{}; methodLines = @{} }
        }
        $module = $modules[$package.name]
        foreach ($class in @($package.classes.class | Where-Object { $_ })) {
            $fileName = $class.filename
            $resolved = $null
            if ([IO.Path]::IsPathRooted($fileName)) { $resolved = $fileName }
            else {
                foreach ($source in $sources) {
                    $candidate = Join-Path $source $fileName
                    if (Test-Path -LiteralPath $candidate) { $resolved = $candidate; break }
                }
                if (-not $resolved -and $sources.Count -gt 0) { $resolved = Join-Path $sources[0] $fileName }
            }
            $file = Get-RelativePath $resolved
            foreach ($line in @($class.lines.line | Where-Object { $_ })) {
                $key = "$file|$($line.number)"
                $hits = [long]$line.hits
                if (-not $module.lines.ContainsKey($key) -or $module.lines[$key] -lt $hits) { $module.lines[$key] = $hits }
                if ($line.branch -eq 'True' -and $line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                    $pair = @([int]$Matches[1], [int]$Matches[2])
                    if (-not $module.branches.ContainsKey($key) -or $module.branches[$key][0] -lt $pair[0]) { $module.branches[$key] = $pair }
                }
            }
            foreach ($method in @($class.methods.method | Where-Object { $_ })) {
                $methodKey = "$file|$($class.name)|$($method.name)|$($method.signature)"
                $covered = @($method.lines.line | Where-Object { $_ -and [long]$_.hits -gt 0 }).Count -gt 0
                if (-not $module.methods.ContainsKey($methodKey)) { $module.methods[$methodKey] = $false }
                if ($covered) { $module.methods[$methodKey] = $true }
            }
        }
    }
}

function Get-Percent([int]$Covered, [int]$Valid) {
    if ($Valid -eq 0) { return $null }
    return [math]::Round(100.0 * $Covered / $Valid, 2)
}

$summaries = @()
foreach ($name in ($modules.Keys | Sort-Object)) {
    $module = $modules[$name]
    $all = @{ files = @{}; lineValid = 0; lineCovered = 0; branchValid = 0; branchCovered = 0; methodValid = 0; methodCovered = 0 }
    $kept = @{ files = @{}; lineValid = 0; lineCovered = 0; branchValid = 0; branchCovered = 0; methodValid = 0; methodCovered = 0 }
    foreach ($key in $module.lines.Keys) {
        $file = $key.Substring(0, $key.LastIndexOf('|'))
        $hit = $module.lines[$key] -gt 0
        foreach ($bucket in @($all) + @(if (-not (Test-Excluded $file)) { $kept })) {
            $bucket.files[$file] = $true; $bucket.lineValid++; if ($hit) { $bucket.lineCovered++ }
        }
    }
    foreach ($key in $module.branches.Keys) {
        $file = $key.Substring(0, $key.LastIndexOf('|'))
        foreach ($bucket in @($all) + @(if (-not (Test-Excluded $file)) { $kept })) {
            $bucket.branchCovered += $module.branches[$key][0]; $bucket.branchValid += $module.branches[$key][1]
        }
    }
    foreach ($key in $module.methods.Keys) {
        $file = $key.Substring(0, $key.IndexOf('|'))
        foreach ($bucket in @($all) + @(if (-not (Test-Excluded $file)) { $kept })) {
            $bucket.methodValid++; if ($module.methods[$key]) { $bucket.methodCovered++ }
        }
    }
    $summaries += New-Object PSObject -Property ([ordered]@{
        module = $name
        sourcesAll = $all.files.Count; sourcesMeasured = $kept.files.Count
        linesValid = $kept.lineValid; linesCovered = $kept.lineCovered; line = Get-Percent $kept.lineCovered $kept.lineValid
        branchesValid = $kept.branchValid; branchesCovered = $kept.branchCovered; branch = Get-Percent $kept.branchCovered $kept.branchValid
        methodsValid = $kept.methodValid; methodsCovered = $kept.methodCovered; method = Get-Percent $kept.methodCovered $kept.methodValid
        lineUnfiltered = Get-Percent $all.lineCovered $all.lineValid; branchUnfiltered = Get-Percent $all.branchCovered $all.branchValid
    })
}

# Threshold enforcement, one module at a time.
$checks = @()
foreach ($rule in @($policy.modules)) {
    $summary = $summaries | Where-Object { $_.module -eq $rule.module }
    $status = 'pass'; $detail = @()
    if (-not $summary) { $status = 'fail'; $detail += 'module missing from coverage' }
    else {
        if ($summary.sourcesMeasured -lt [int]$rule.minimumSources) { $status = 'fail'; $detail += "sources $($summary.sourcesMeasured) < $($rule.minimumSources)" }
        if ($null -ne $rule.line -and ($null -eq $summary.line -or $summary.line -lt [double]$rule.line)) { $status = 'fail'; $detail += "line $($summary.line)% < $($rule.line)%" }
        if ($null -ne $rule.branch -and ($null -eq $summary.branch -or $summary.branch -lt [double]$rule.branch)) { $status = 'fail'; $detail += "branch $($summary.branch)% < $($rule.branch)%" }
    }
    $checks += New-Object PSObject -Property ([ordered]@{ module = $rule.module; layer = $rule.layer; line = $rule.line; branch = $rule.branch; minimumSources = $rule.minimumSources; status = $status; detail = ($detail -join '; ') })
}

$result = [ordered]@{
    createdAt = (Get-Date -Format o)
    input = Get-RelativePath $InputDirectory
    thresholds = Get-RelativePath $ThresholdsPath
    exclusions = $exclusions
    reports = $reportInfo
    modules = $summaries
    checks = $checks
    problems = @($problems)
}
$result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $InputDirectory 'coverage-summary.json')

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add('| Module | Sources (measured/all) | Line % | Branch % | Method % | Line % unfiltered |')
$markdown.Add('|---|---:|---:|---:|---:|---:|')
foreach ($summary in $summaries) {
    $markdown.Add("| $($summary.module) | $($summary.sourcesMeasured)/$($summary.sourcesAll) | $($summary.line) ($($summary.linesCovered)/$($summary.linesValid)) | $($summary.branch) ($($summary.branchesCovered)/$($summary.branchesValid)) | $($summary.method) ($($summary.methodsCovered)/$($summary.methodsValid)) | $($summary.lineUnfiltered) |")
}
$markdown.Add('')
$markdown.Add('| Module | Layer | Min line % | Min branch % | Min sources | Result |')
$markdown.Add('|---|---|---:|---:|---:|---|')
foreach ($check in $checks) { $markdown.Add("| $($check.module) | $($check.layer) | $($check.line) | $($check.branch) | $($check.minimumSources) | $($check.status) $($check.detail) |") }
$markdown | Set-Content (Join-Path $InputDirectory 'coverage-summary.md')
$markdown | Out-Host
$problems | ForEach-Object { Write-Host "PROBLEM: $_" }

$failedChecks = @($checks | Where-Object { $_.status -ne 'pass' })
if ($problems.Count -gt 0) { Write-Host 'Coverage report: INVALID'; exit 1 }
if (-not $NoEnforce -and $failedChecks.Count -gt 0) { Write-Host "Coverage thresholds: FAILED ($($failedChecks.Count) module(s))"; exit 1 }
Write-Host 'Coverage report: OK'
exit 0
