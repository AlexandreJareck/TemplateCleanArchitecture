<#
.SYNOPSIS
    Phase 22 repository secret scan: committed configuration carries no secret values, and the compromised JWT signing
    key is used nowhere.
.DESCRIPTION
    1. Structural rule: every tracked appsettings*.json and launchSettings.json is parsed; a non-empty string under a
       property whose name ends in Key, Password, Pwd, Secret or Token, or a connection string with a non-empty
       Password/Pwd segment, is a finding. Compose files and .env.example may reference JwtSettings__Key only as the
       ${JwtSettings__Key} interpolation or the CHANGE_ME placeholder.
    2. Compromised value: the JwtSettings:Key committed before Phase 22 is identified only by a fingerprint (length,
       character set and SHA-256), because the D6 history rewrite removed it from Git. Every alphanumeric window of that
       length in every tracked and untracked, non-ignored file, plus the optional -EvidencePath folders, is hashed and
       compared. The value itself is not stored anywhere in the repository.
    Findings report file paths and key names only; no value is ever printed. Exit code 1 on any finding.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\secret-scan.ps1 -EvidencePath artifacts\validation
#>
param(
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
# Fingerprint of the compromised pre-Phase 22 JwtSettings:Key: 32 ASCII letters/digits with this SHA-256 (UTF-8).
$compromisedLength = 32
$compromisedSha256 = 'bcb4fc5a5b0591abf79db8006e56197fd97f79932669c965bb8f0c56bb4c2ecd'
$sha256 = [System.Security.Cryptography.SHA256]::Create()
function Test-Compromised([string]$Text) {
    foreach ($run in [regex]::Matches($Text, "[A-Za-z0-9]{$compromisedLength,}")) {
        for ($offset = 0; $offset -le $run.Length - $compromisedLength; $offset++) {
            $window = $run.Value.Substring($offset, $compromisedLength)
            $hash = [BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($window))).Replace('-', '').ToLowerInvariant()
            if ($hash -eq $compromisedSha256) { return $true }
        }
    }
    return $false
}

if (-not $SkipCompromisedValue) {

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
        if (Test-Compromised $text) { $findings.Add("compromised-value: $($item.FullName.Substring($repositoryRoot.Length).TrimStart('\') -replace '\\', '/')") }
    }
    "compromised-value: fingerprint searched in $scanned file(s)"
}

if ($findings.Count -gt 0) {
    $findings | Sort-Object -Unique | ForEach-Object { "FINDING $_" }
    "secret-scan: FAILED ($($findings.Count) finding(s))"
    exit 1
}
'secret-scan: OK'
