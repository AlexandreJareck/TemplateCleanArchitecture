<#
.SYNOPSIS
    Isolated validation path for the optional legacy VSIX (ExtensionVsix/Template). Not part of the application gate.
.DESCRIPTION
    Phase 20. Locates a Visual Studio instance with MSBuild through vswhere (no edition-specific path; default range
    [17.0,19.0) = Visual Studio 2022 or 2026, newest stable first, -Prerelease to accept Insiders), restores and
    rebuilds only ExtensionVsix/Template/Template.csproj with that MSBuild (DeployExtension=false, so a
    build never deploys into any Visual Studio instance), then inspects the produced .vsix: non-empty, SHA-256, zip
    entries, extension.vsixmanifest identity/installation targets/architecture/prerequisites, and resolved VSSDK
    packages (all 17.x, no Visual Studio 18 target). NuGet audit (direct and transitive) runs during restore.
    Evidence goes to artifacts/validation/vsix/<Label> (git-ignored).

    Optional, explicit switches act only on the Visual Studio experimental hive (<version>_<instance><RootSuffix>,
    default suffix Exp) of the selected instance, never on the normal profile:
      -Install    VSIXInstaller /quiet /rootSuffix:<RootSuffix> into the experimental hive and verify the extracted
                  manifest in the per-user extensions folder of that hive.
      -Smoke      starts devenv /RootSuffix <RootSuffix> /Log <file> /Command File.Exit, waits for exit and scans the
                  activity log for errors and entries that reference the extension. This is a startup check only:
                  the extension has no package or command, and a devenv that exits before the shell loads still
                  passes, so read ActivityLog.xml before claiming a load.
      -Uninstall  VSIXInstaller /uninstall:<identity> on the same experimental hive.
    Exit code 0 only when every requested step passed; any restore/build/packaging/inspection/install failure is
    nonzero. dotnet build is not a substitute for this path.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1 -Label exp-check -Install -Smoke
#>
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Label = 'release',
    # Visual Studio product range accepted for the build toolchain. This selects only the MSBuild used to build; the
    # packages and the manifest stay on the VSSDK 17.x line. Use '[18.0,19.0)' -Prerelease for Visual Studio 2026 Insiders.
    [string]$VisualStudioVersionRange = '[17.0,19.0)',
    # Also accept prerelease (Insiders/Preview) Visual Studio instances.
    [switch]$Prerelease,
    [string]$RootSuffix = 'Exp',
    [switch]$Install,
    [switch]$Smoke,
    [switch]$Uninstall,
    [int]$SmokeTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
$projectDirectory = Join-Path $repositoryRoot 'ExtensionVsix\Template'
$project = Join-Path $projectDirectory 'Template.csproj'
$sourceManifestPath = Join-Path $projectDirectory 'source.extension.vsixmanifest'
$output = Join-Path $repositoryRoot "artifacts\validation\vsix\$Label"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$summaryPath = Join-Path $output 'summary.txt'
"VSIX validation $Label started $(Get-Date -Format o)" | Set-Content $summaryPath -Encoding utf8
$script:failures = 0

function Write-Check([bool]$Passed, [string]$Name, [string]$Detail = '') {
    $line = '{0} {1}{2}' -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $(if ($Detail) { " - $Detail" } else { '' })
    if (-not $Passed) { $script:failures++ }
    Write-Host $line
    $line | Add-Content $summaryPath -Encoding utf8
}

function Write-Info([string]$Text) {
    Write-Host $Text
    "INFO $Text" | Add-Content $summaryPath -Encoding utf8
}

function Complete-Validation {
    "VSIX validation $Label finished $(Get-Date -Format o) failures=$script:failures" | Add-Content $summaryPath -Encoding utf8
    if ($script:failures -gt 0) { exit 1 }
    exit 0
}

function Test-SeventeenRange([string]$Range) {
    # Accepts [17.x,18.0) or [17.x,) style ranges only when the upper bound stays below 18.
    if ($Range -notmatch '^\[(?<min>\d+)(\.[\d.]+)?\s*,\s*(?<max>[\d.]*)\s*(?<close>[\)\]])$') { return $false }
    if ([int]$Matches.min -ne 17) { return $false }
    if (-not $Matches.max) { return $false }
    $max = [version]($Matches.max + $(if ($Matches.max -notmatch '\.') { '.0' } else { '' }))
    return ($max -le [version]'18.0' -and -not ($max -eq [version]'18.0' -and $Matches.close -eq ']'))
}

# 1. Toolchain discovery (stable vswhere location documented by Microsoft).
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    Write-Check $false 'vswhere' 'Visual Studio Installer (vswhere.exe) not found; install Visual Studio 2022 or Build Tools 2022'
    Complete-Validation
}
$vswhereArguments = @('-version', $VisualStudioVersionRange, '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-format', 'json')
if ($Prerelease) { $vswhereArguments += '-prerelease' }
$instances = @(& $vswhere @vswhereArguments | ConvertFrom-Json)
if ($instances.Count -eq 0) {
    Write-Check $false 'visual-studio' "no Visual Studio instance in $VisualStudioVersionRange with Microsoft.Component.MSBuild (stable only unless -Prerelease)"
    Complete-Validation
}
$instance = $instances | Sort-Object { [version]$_.installationVersion } -Descending | Select-Object -First 1
$msbuild = Join-Path $instance.installationPath 'MSBuild\Current\Bin\MSBuild.exe'
$msbuildVersion = (& $msbuild -version -nologo | Select-Object -Last 1).Trim()
$components = @(& $vswhere -path $instance.installationPath -include packages -format json | ConvertFrom-Json | ForEach-Object { $_.packages } | ForEach-Object { $_.id })
$hasVssdk = ($components -contains 'Microsoft.VisualStudio.Component.VSSDK') -or ($components -contains 'Microsoft.VisualStudio.Component.VSSDKBuildTools')
$hasTargetingPack = Test-Path (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2')
Write-Info ("Visual Studio: {0} {1} (product {2}, instance {3})" -f $instance.displayName, $instance.catalog.productDisplayVersion, $instance.installationVersion, $instance.instanceId)
Write-Info "MSBuild: <VS>\MSBuild\Current\Bin\MSBuild.exe $msbuildVersion"
Write-Check (Test-Path $msbuild) 'msbuild' $msbuildVersion
Write-Check $hasVssdk 'vssdk-component' 'Visual Studio extension development (Microsoft.VisualStudio.Component.VSSDK or VSSDKBuildTools)'
Write-Check $hasTargetingPack 'netfx-472-targeting-pack' '.NET Framework 4.7.2 reference assemblies'
if ($script:failures -gt 0) { Complete-Validation }

# 2. Isolated restore (NuGet audit of direct and transitive packages) and Release/Debug rebuild of the VSIX project only.
$restoreLog = Join-Path $output 'restore.log'
& $msbuild $project /t:Restore /p:Configuration=$Configuration /p:NuGetAudit=true /p:NuGetAuditMode=all /p:NuGetAuditLevel=low /nologo /nr:false /m:1 /v:minimal "/flp:LogFile=$restoreLog;Verbosity=normal" | Out-Host
$restoreExit = $LASTEXITCODE
$auditWarnings = @(Select-String -Path $restoreLog -Pattern 'warning NU190[0-4]' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique)
Write-Check ($restoreExit -eq 0) 'restore' "exit=$restoreExit"
Write-Check ($auditWarnings.Count -eq 0) 'nuget-audit' "$($auditWarnings.Count) NU190x warning(s)"
$auditWarnings | ForEach-Object { Write-Info "audit: $_" }
if ($restoreExit -ne 0) { Complete-Validation }

$buildLog = Join-Path $output 'build.log'
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU /p:DeployExtension=false /nologo /nr:false /m:1 /v:minimal "/flp:LogFile=$buildLog;Verbosity=normal" | Out-Host
$buildExit = $LASTEXITCODE
$buildWarnings = @(Select-String -Path $buildLog -Pattern ': warning ' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique)
$buildErrors = @(Select-String -Path $buildLog -Pattern ': error ' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique)
Write-Check ($buildExit -eq 0 -and $buildErrors.Count -eq 0) 'build' "exit=$buildExit errors=$($buildErrors.Count) warnings=$($buildWarnings.Count)"
$buildWarnings | ForEach-Object { Write-Info "build warning: $_" }
$buildErrors | ForEach-Object { Write-Info "build error: $_" }
if ($buildExit -ne 0) { Complete-Validation }

# 3. Resolved VSSDK packages: every Microsoft.VisualStudio.SDK / VSSDK package must stay on 17.x.
$assets = Get-Content -Raw (Join-Path $projectDirectory 'obj\project.assets.json') | ConvertFrom-Json
$libraries = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name } | Sort-Object)
$libraries | Set-Content (Join-Path $output 'resolved-packages.txt') -Encoding utf8
Write-Info "resolved packages: $($libraries.Count) (resolved-packages.txt)"
$vssdk = @($libraries | Where-Object { $_ -match '^(Microsoft\.VisualStudio\.SDK|Microsoft\.VSSDK\.[^/]+|Microsoft\.VsSDK\.[^/]+)/' })
foreach ($entry in $vssdk) { Write-Check ($entry -match '/17\.') "vssdk-17x $entry" }
foreach ($required in 'Microsoft.VisualStudio.SDK', 'Microsoft.VSSDK.BuildTools') {
    Write-Check (@($vssdk | Where-Object { $_ -like "$required/*" }).Count -eq 1) "resolved $required"
}
$eighteen = @($libraries | Where-Object { $_ -match '^Microsoft\.(VisualStudio|VSSDK|VsSDK)\.[^/]*/18\.' })
Write-Check ($eighteen.Count -eq 0) 'no-visual-studio-18-packages' ($eighteen -join ', ')

# 4. Artifact and package inspection.
$vsixPath = Join-Path $projectDirectory "bin\$Configuration\Template.vsix"
$vsixExists = Test-Path $vsixPath
Write-Check ($vsixExists -and (Get-Item $vsixPath).Length -gt 0) 'vsix-artifact' "bin\$Configuration\Template.vsix"
if (-not $vsixExists) { Complete-Validation }
$copy = Join-Path $output 'Template.vsix'
Copy-Item $vsixPath $copy -Force
$hash = (Get-FileHash $copy -Algorithm SHA256).Hash
"$hash  Template.vsix" | Set-Content (Join-Path $output 'Template.vsix.sha256') -Encoding ascii
Write-Info ("artifact: artifacts/validation/vsix/{0}/Template.vsix size={1} sha256={2}" -f $Label, (Get-Item $copy).Length, $hash)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($copy)
try {
    $entries = @($zip.Entries | ForEach-Object { '{0} {1}' -f $_.FullName, $_.Length })
    $entries | Set-Content (Join-Path $output 'contents.txt') -Encoding utf8
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    foreach ($name in 'extension.vsixmanifest', 'manifest.json', 'catalog.json', '[Content_Types].xml') {
        Write-Check ($names -contains $name) "vsix-entry $name"
    }
    $unexpected = @($names | Where-Object { $_ -match '\.(dll|pdb|pkgdef)$' })
    Write-Check ($unexpected.Count -eq 0) 'no-unexpected-binaries' 'IncludeAssemblyInVSIXContainer/GeneratePkgDefFile are false'
    $reader = New-Object IO.StreamReader($zip.GetEntry('extension.vsixmanifest').Open())
    $packedManifestText = $reader.ReadToEnd()
    $reader.Dispose()
    $reader = New-Object IO.StreamReader($zip.GetEntry('catalog.json').Open())
    $catalogText = $reader.ReadToEnd()
    $reader.Dispose()
}
finally { $zip.Dispose() }
$packedManifestText | Set-Content (Join-Path $output 'extension.vsixmanifest') -Encoding utf8
$catalogText | Set-Content (Join-Path $output 'catalog.json') -Encoding utf8

$ns = @{ v = 'http://schemas.microsoft.com/developer/vsx-schema/2011' }
[xml]$sourceManifest = Get-Content -Raw $sourceManifestPath
[xml]$packedManifest = $packedManifestText
$sourceIdentity = (Select-Xml -Xml $sourceManifest -XPath '//v:Metadata/v:Identity' -Namespace $ns).Node
$identity = (Select-Xml -Xml $packedManifest -XPath '//v:Metadata/v:Identity' -Namespace $ns).Node
Write-Check ($identity.Id -eq $sourceIdentity.Id -and $identity.Version -eq $sourceIdentity.Version -and $identity.Publisher -eq $sourceIdentity.Publisher) 'manifest-identity' ("{0} {1} ({2})" -f $identity.Id, $identity.Version, $identity.Publisher)
$targets = @((Select-Xml -Xml $packedManifest -XPath '//v:Installation/v:InstallationTarget' -Namespace $ns) | ForEach-Object { $_.Node })
Write-Check ($targets.Count -gt 0) 'installation-targets' "$($targets.Count) target(s)"
foreach ($target in $targets) {
    $architectures = @($target.ProductArchitecture)
    Write-Check (Test-SeventeenRange $target.Version) "installation-target-17x $($target.Id)" $target.Version
    Write-Check ($architectures -contains 'amd64') "installation-target-amd64 $($target.Id)" ($architectures -join ',')
}
foreach ($prerequisite in @((Select-Xml -Xml $packedManifest -XPath '//v:Prerequisites/v:Prerequisite' -Namespace $ns) | ForEach-Object { $_.Node })) {
    Write-Check (Test-SeventeenRange $prerequisite.Version) "prerequisite-17x $($prerequisite.Id)" $prerequisite.Version
}
$catalog = $catalogText | ConvertFrom-Json
$catalogVsix = @($catalog.packages | Where-Object { $_.type -eq 'Vsix' })
Write-Check ($catalogVsix.Count -eq 1 -and $catalogVsix[0].id -eq $identity.Id -and $catalogVsix[0].version -eq $identity.Version) 'catalog-identity'
Write-Check ($catalogText -notmatch '18\.0[^)]*\]|\[18\.') 'catalog-no-visual-studio-18-range'

# 5. Optional experimental-instance installation, smoke and uninstall (never the normal profile).
$hive = '{0}.0_{1}{2}' -f ([version]$instance.installationVersion).Major, $instance.instanceId, $RootSuffix
$installer = Join-Path $instance.installationPath 'Common7\IDE\VSIXInstaller.exe'
$extensionsRoot = Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio\$hive\Extensions"
function Find-InstalledManifest {
    if (-not (Test-Path $extensionsRoot)) { return $null }
    Get-ChildItem $extensionsRoot -Recurse -Filter 'extension.vsixmanifest' -ErrorAction SilentlyContinue |
        Where-Object { (Get-Content -Raw $_.FullName) -match [regex]::Escape($identity.Id) } | Select-Object -First 1
}
function Invoke-VsixInstaller([string[]]$Arguments, [string]$LogName) {
    $logFile = "vsix-$Label-$LogName-$(Get-Date -Format yyyyMMddHHmmss).log"
    $process = Start-Process -FilePath $installer -ArgumentList ($Arguments + @('/quiet', "/rootSuffix:$RootSuffix", "/instanceIds:$($instance.instanceId)", "/logFile:$logFile")) -Wait -PassThru
    $tempLog = Join-Path $env:TEMP $logFile
    if (Test-Path $tempLog) { Copy-Item $tempLog (Join-Path $output "$LogName-vsixinstaller.log") -Force }
    return $process.ExitCode
}
if ($Install -or $Smoke -or $Uninstall) {
    if ([string]::IsNullOrWhiteSpace($RootSuffix)) { Write-Check $false 'root-suffix' 'an experimental root suffix is required'; Complete-Validation }
    Write-Info "experimental hive: $hive"
}
if ($Install) {
    $code = Invoke-VsixInstaller @("`"$copy`"") 'install'
    Write-Check ($code -eq 0) 'install-experimental' "VSIXInstaller exit=$code"
    $installed = Find-InstalledManifest
    Write-Check ($null -ne $installed) 'installed-manifest' $(if ($installed) { $installed.Directory.FullName.Replace($env:LOCALAPPDATA, '%LOCALAPPDATA%') } else { 'not found' })
    if ($installed) {
        [xml]$installedManifest = Get-Content -Raw $installed.FullName
        $installedIdentity = (Select-Xml -Xml $installedManifest -XPath '//v:Metadata/v:Identity' -Namespace $ns).Node
        Write-Check ($installedIdentity.Version -eq $identity.Version) 'installed-version' $installedIdentity.Version
    }
}
if ($Smoke) {
    $devenv = Join-Path $instance.installationPath 'Common7\IDE\devenv.exe'
    $activityLog = Join-Path $output 'ActivityLog.xml'
    if (Test-Path $activityLog) { Remove-Item $activityLog }
    $started = Get-Date
    $process = Start-Process -FilePath $devenv -ArgumentList @('/RootSuffix', $RootSuffix, '/Log', "`"$activityLog`"", '/Command', 'File.Exit') -PassThru
    $exited = $process.WaitForExit($SmokeTimeoutSeconds * 1000)
    if (-not $exited) { Stop-Process -Id $process.Id -Force }
    Write-Check $exited 'smoke-devenv-start-exit' ("devenv /RootSuffix {0} exited={1} code={2} seconds={3:N0}" -f $RootSuffix, $exited, $(if ($exited) { $process.ExitCode } else { 'killed' }), ((Get-Date) - $started).TotalSeconds)
    Write-Check (Test-Path $activityLog) 'smoke-activity-log'
    if (Test-Path $activityLog) {
        [xml]$log = Get-Content -Raw $activityLog
        $logEntries = @($log.activity.entry)
        $errors = @($logEntries | Where-Object { $_.type -eq 'Error' })
        $related = @($logEntries | Where-Object { "$($_.description) $($_.path)" -match [regex]::Escape($identity.Id) -or "$($_.description)" -match 'Template\.vsix' })
        $relatedErrors = @($related | Where-Object { $_.type -ne 'Information' })
        Write-Info "activity log entries=$($logEntries.Count) errors=$($errors.Count) extension-related=$($related.Count)"
        $errors | ForEach-Object { Write-Info ("activity error: [{0}] {1}" -f $_.source, ("$($_.description)" -replace '\s+', ' ').Substring(0, [Math]::Min(300, ("$($_.description)" -replace '\s+', ' ').Length))) }
        $related | ForEach-Object { Write-Info ("extension entry: {0} [{1}] {2}" -f $_.type, $_.source, ("$($_.description)" -replace '\s+', ' ')) }
        Write-Check ($relatedErrors.Count -eq 0) 'smoke-no-extension-load-errors' "$($relatedErrors.Count) warning/error entries reference the extension"
    }
    $stillInstalled = Find-InstalledManifest
    Write-Check ($null -ne $stillInstalled) 'smoke-extension-still-installed'
}
if ($Uninstall) {
    $code = Invoke-VsixInstaller @("/uninstall:$($identity.Id)") 'uninstall'
    Write-Check ($code -eq 0) 'uninstall-experimental' "VSIXInstaller exit=$code"
    Write-Check ($null -eq (Find-InstalledManifest)) 'uninstalled-manifest-removed'
}

Complete-Validation
