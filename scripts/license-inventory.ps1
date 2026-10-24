<#
.SYNOPSIS
    Writes a direct and transitive NuGet license inventory for the application solution filter (Phase 21).
.DESCRIPTION
    Resolves the package graph with `dotnet list <slnf> package --include-transitive --format json` (requires a prior
    restore) and reads each package's license metadata from its .nuspec in the local NuGet global packages folder
    (no network access, no package download). Output: artifacts/validation/licenses/<Label>/licenses.csv and
    summary.txt. A package whose license cannot be read is reported as "unknown". The exit code is 1 when any package
    is unknown or uses a license expression outside -AllowedLicenses, 0 otherwise.
#>
param(
    [string]$Label = (Get-Date -Format 'yyyyMMddHHmmss'),
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [string]$Target = 'TemplateCleanArchitecture.App.slnf',
    # Policy approved by the owner on 2026-10-14 (Phase 21): permissive SPDX licenses, the xunit Apache-2.0 license
    # URL, and two named exceptions accepted by explicit owner decision: MediatR (RPL-1.5 or the Lucky Penny commercial
    # license; the obligation is a documented risk) and the Microsoft Visual Studio Container Tools Targets EULA
    # (design-time tooling).
    [string[]]$AllowedLicenses = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'PostgreSQL'),
    [string[]]$AllowedLicenseUrls = @('https://raw.githubusercontent.com/xunit/xunit/master/license.txt'),
    [string[]]$AcceptedPackages = @('MediatR', 'Microsoft.VisualStudio.Azure.Containers.Tools.Targets'),
    # Phase 30: packages whose nuspec declares the license as a file. The file is read from the package and accepted only
    # when its SHA-256 matches the reviewed text, which must be an allowed license (no new license is admitted here).
    # Apache.Arrow 22.1.0 (transitive of Azure.Storage.Blobs 12.30.1): LICENSE.txt is the Apache-2.0 text plus the
    # Apache-2.0 FlatBuffers notice, reviewed on 2026-10-23.
    [hashtable]$VerifiedLicenseFiles = @{
        'Apache.Arrow/22.1.0' = @{ License = 'Apache-2.0'; Sha256 = 'f49cc7be44c8d4b6efffed6ac00026f2ff2146310f040350b50be30ed3f9c570' }
    },
    # Phase 31: packages whose nuspec still declares only a legacy license URL. Accepted only for that package version and
    # URL, and only as an allowed license (no new license is admitted here). System.Reactive.Compatibility 4.4.1
    # (transitive of Microsoft.Azure.DurableTask.Core 3.8.0): the fwlink redirects to github.com/dotnet/reactive LICENSE,
    # the MIT text of the .NET Foundation, like the System.Reactive 4.4.1 nuspec expression; reviewed on 2026-10-24.
    [hashtable]$VerifiedLicenseUrls = @{
        'System.Reactive.Compatibility/4.4.1' = @{ License = 'MIT'; Url = 'http://go.microsoft.com/fwlink/?LinkID=261272' }
    }
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
$outputDirectory = Join-Path $repositoryRoot "artifacts\validation\licenses\$Label"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$json = & $DotNetPath list $Target package --include-transitive --format json | Out-String
if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed with exit code $LASTEXITCODE" }
$graph = $json | ConvertFrom-Json

$packages = @{}
foreach ($project in $graph.projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.path)
    $scope = if ($project.path -match '[\\/]Tests[\\/]') { 'test' } else { 'production' }
    foreach ($framework in @($project.frameworks)) {
        foreach ($kind in 'topLevelPackages', 'transitivePackages') {
            foreach ($package in @($framework.$kind)) {
                if (-not $package) { continue }
                $key = "$($package.id)/$($package.resolvedVersion)"
                if (-not $packages.ContainsKey($key)) {
                    $packages[$key] = [pscustomobject]@{
                        Id = $package.id; Version = $package.resolvedVersion; Direct = $false
                        Scopes = New-Object System.Collections.Generic.SortedSet[string]
                        Projects = New-Object System.Collections.Generic.SortedSet[string]
                    }
                }
                if ($kind -eq 'topLevelPackages') { $packages[$key].Direct = $true }
                [void]$packages[$key].Scopes.Add($scope)
                [void]$packages[$key].Projects.Add($projectName)
            }
        }
    }
}

$globalPackages = (& $DotNetPath nuget locals global-packages --list | Out-String) -replace '^.*?:\s*', ''
$globalPackages = $globalPackages.Trim()

$rows = foreach ($package in $packages.Values | Sort-Object Id, Version) {
    $folder = Join-Path $globalPackages (Join-Path $package.Id.ToLowerInvariant() $package.Version.ToLowerInvariant())
    $nuspec = Get-ChildItem -Path $folder -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    $type = 'unknown'; $license = ''; $url = ''
    if ($nuspec) {
        [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
        $metadata = $xml.package.metadata
        if ($metadata.license) {
            $type = $metadata.license.type
            $license = if ($type -eq 'expression') { $metadata.license.'#text' } else { $metadata.license.'#text' }
        }
        if ($metadata.licenseUrl) { $url = [string]$metadata.licenseUrl }
        if (-not $metadata.license -and $url) { $type = 'url' }
    }
    $allowed = switch ($type) {
        'expression' {
            $terms = $license -split '\s+(?:OR|AND|WITH)\s+' | ForEach-Object { $_.Trim('(', ')', ' ') }
            if ($license -match '\sOR\s') { [bool]($terms | Where-Object { $AllowedLicenses -contains $_ }) }
            else { -not ($terms | Where-Object { $AllowedLicenses -notcontains $_ }) }
        }
        'url' {
            $verifiedUrl = $VerifiedLicenseUrls["$($package.Id)/$($package.Version)"]
            if ($AllowedLicenseUrls -contains $url) { $true }
            elseif ($verifiedUrl -and $verifiedUrl.Url -eq $url -and $AllowedLicenses -contains $verifiedUrl.License) {
                $license = "$($verifiedUrl.License) (verified URL)"
                $true
            }
            else { $false }
        }
        'file' {
            $verified = $VerifiedLicenseFiles["$($package.Id)/$($package.Version)"]
            $licenseFile = if ($verified -and $license) { Join-Path $folder $license } else { $null }
            if ($licenseFile -and (Test-Path $licenseFile) -and $AllowedLicenses -contains $verified.License -and
                (Get-FileHash -Algorithm SHA256 -LiteralPath $licenseFile).Hash.ToLowerInvariant() -eq $verified.Sha256) {
                $license = "$($verified.License) (verified file $license)"
                $true
            }
            else { $false }
        }
        default { $false }
    }
    $accepted = $AcceptedPackages -contains $package.Id
    if ($accepted) { $allowed = $true }
    [pscustomobject]@{
        Id = $package.Id; Version = $package.Version; Direct = $package.Direct
        Scope = ($package.Scopes -join '+'); LicenseType = $type; License = $license; LicenseUrl = $url
        Allowed = $allowed; AcceptedException = $accepted; Projects = ($package.Projects -join ';')
    }
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $outputDirectory 'licenses.csv')
$rejected = @($rows | Where-Object { -not $_.Allowed })
$summary = @(
    "Packages: $(@($rows).Count) (direct: $(@($rows | Where-Object Direct).Count))"
    "Production-scope packages: $(@($rows | Where-Object { $_.Scope -match 'production' }).Count)"
    'By license:'
    ($rows | Group-Object { if ($_.License) { $_.License } elseif ($_.LicenseUrl) { $_.LicenseUrl } else { 'unknown' } } |
        Sort-Object Count -Descending | ForEach-Object { "  $($_.Count)  $($_.Name)" })
    "Allowed licenses: $($AllowedLicenses -join ', ')"
    "Allowed license URLs: $($AllowedLicenseUrls -join ', ')"
    "Accepted package exceptions: $(($rows | Where-Object AcceptedException | ForEach-Object { "$($_.Id) $($_.Version) ($($_.LicenseType) $($_.License))" }) -join '; ')"
    "Not allowed or unknown: $($rejected.Count)"
    ($rejected | ForEach-Object { "  $($_.Id) $($_.Version) [$($_.Scope)] $($_.LicenseType) $($_.License)$($_.LicenseUrl)" })
)
$summary | Set-Content -Encoding UTF8 (Join-Path $outputDirectory 'summary.txt')
$summary | Write-Host
if ($rejected.Count -gt 0) { exit 1 }
exit 0
