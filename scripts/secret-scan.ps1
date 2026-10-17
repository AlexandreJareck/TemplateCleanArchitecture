<#
.SYNOPSIS
    Phase 22 repository secret scan: committed configuration carries no secret values, and the compromised JWT signing
    key is used nowhere.
.DESCRIPTION
    1. Structural rule: every tracked appsettings*.json and launchSettings.json is parsed; a non-empty string under a
       property whose name ends in Key, Password, Pwd, Secret or Token, or a connection string with a non-empty
       Password/Pwd segment, is a finding. Compose files and .env.example may reference JwtSettings__Key only as the
       ${JwtSettings__Key} interpolation or the CHANGE_ME placeholder.
    2. Compromised value: the JwtSettings:Key committed at -CompromisedRef (the pre-Phase 22 state) is read into memory
       from Git and searched in every tracked and untracked, non-ignored file, plus the optional -EvidencePath folders.
    Findings report file paths and key names only; no value is ever printed. Exit code 1 on any finding.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\secret-scan.ps1 -EvidencePath artifacts\validation
#>
param(
    # Commit that still holds the old committed key (Phase 21 merge).
    [string]$CompromisedRef = '622e808',
    # Run only the structural rule (for example in a mutation check).
    [switch]$SkipCompromisedValue,
    # Extra folders to search for the compromised value (for example run evidence under artifacts/validation).
    [string[]]$EvidencePath = @(),
    [int]$MaxFileBytes = 20MB
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
$findings = New-Object System.Collections.Generic.List[string]

function Test-Json([string]$File, $Node, [string]$Path, [string]$Name) {
    if ($null -eq $Node) { return }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            $childPath = if ($Path) { "${Path}:$($property.Name)" } else { $property.Name }
            Test-Json $File $property.Value $childPath $property.Name
        }
    }
    elseif ($Node -is [System.Array]) {
        for ($index = 0; $index -lt $Node.Count; $index++) { Test-Json $File $Node[$index] "${Path}:$index" $Name }
    }
    elseif ($Node -is [string]) {
        $secretName = $Name -match '(?i)(key|password|pwd|secret|token)$'
        $credential = $Node -match '(?i)(^|[;,])\s*(password|pwd)\s*=\s*[^;,\s]'
        if (($secretName -and $Node.Length -gt 0) -or $credential) { $findings.Add("structural: ${File}: $Path") }
    }
}

# ---------------------------------------------------------------- 1. Structural rule on tracked configuration
$settingsFiles = @(git ls-files -- '*appsettings*.json' '*Properties/launchSettings.json')
foreach ($file in $settingsFiles) {
    try { $json = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { $findings.Add("structural: ${file}: unreadable JSON"); continue }
    Test-Json $file $json '' ''
}
$composeFiles = @(git ls-files -- 'docker-compose*.yml' '.env.example')
foreach ($file in $composeFiles) {
    $number = 0
    foreach ($line in Get-Content $file) {
        $number++
        if ($line.Trim() -match '^(-\s*)?JwtSettings__Key\s*[:=](?<value>.*)$') {
            $value = $Matches['value'].Trim()
            if ($value -and $value -ne '${JwtSettings__Key}' -and $value -ne 'CHANGE_ME') { $findings.Add("structural: ${file}:${number}: JwtSettings__Key") }
        }
    }
}
"structural: $($settingsFiles.Count) settings file(s), $($composeFiles.Count) compose/env template file(s)"

# ---------------------------------------------------------------- 2. Compromised value search
if (-not $SkipCompromisedValue) {
    $compromised = @()
    foreach ($file in @(git ls-tree -r --name-only $CompromisedRef -- 'Src/Presentation/Template.WebApi')) {
        if ($file -notmatch 'appsettings[^/]*\.json$') { continue }
        $json = (git show "${CompromisedRef}:$file" | Out-String) | ConvertFrom-Json
        if ($json.JwtSettings.Key) { $compromised += [string]$json.JwtSettings.Key }
    }
    $compromised = @($compromised | Sort-Object -Unique)
    if ($compromised.Count -eq 0) { throw "No JwtSettings:Key found at $CompromisedRef; pass the commit that still holds it, or -SkipCompromisedValue." }

    $candidates = @(git ls-files) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique | ForEach-Object { Join-Path $repositoryRoot $_ }
    foreach ($folder in $EvidencePath) {
        if (Test-Path $folder) { $candidates += @(Get-ChildItem $folder -Recurse -File | ForEach-Object { $_.FullName }) }
    }
    $scanned = 0
    foreach ($candidate in $candidates) {
        $item = Get-Item -LiteralPath $candidate -ErrorAction SilentlyContinue
        if (-not $item -or $item.PSIsContainer -or $item.Length -gt $MaxFileBytes) { continue }
        $scanned++
        # Share read/write so files still being written (for example this step's own log) can be read.
        try {
            $stream = New-Object System.IO.FileStream($item.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try { $text = (New-Object System.IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
        }
        catch {
            $findings.Add("unreadable: $($item.FullName.Substring($repositoryRoot.Length).TrimStart('\') -replace '\\', '/')")
            continue
        }
        foreach ($value in $compromised) {
            if ($text.Contains($value)) { $findings.Add("compromised-value: $($item.FullName.Substring($repositoryRoot.Length).TrimStart('\') -replace '\\', '/')") }
        }
    }
    "compromised-value: $($compromised.Count) value(s) from $CompromisedRef searched in $scanned file(s)"
}

if ($findings.Count -gt 0) {
    $findings | Sort-Object -Unique | ForEach-Object { "FINDING $_" }
    "secret-scan: FAILED ($($findings.Count) finding(s))"
    exit 1
}
'secret-scan: OK'
