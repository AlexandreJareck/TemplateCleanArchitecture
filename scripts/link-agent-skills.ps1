<#
.SYNOPSIS
    Exposes the canonical .agents/skills directory to Claude Code as .claude/skills.

.DESCRIPTION
    .agents/skills is the single source of truth for agent skills (read directly by Codex).
    Claude Code only discovers project skills under .claude/skills, so this script links that
    path to .agents/skills instead of copying it: a directory junction on Windows (no admin or
    Developer Mode required) or a symbolic link elsewhere. The link is git-ignored and local to
    each checkout. The script is idempotent; run it once after cloning.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot '.agents\skills'
$claudeDir = Join-Path $repoRoot '.claude'
$link = Join-Path $claudeDir 'skills'

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Canonical skills directory not found: $source"
}

if (-not (Test-Path -LiteralPath $claudeDir)) {
    New-Item -ItemType Directory -Path $claudeDir | Out-Null
}

$existing = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $isLink = ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    if (-not $isLink) {
        throw "$link exists and is not a link. Move its contents into .agents\skills and remove it before running this script."
    }

    $target = @($existing.Target)[0]
    if ($null -ne $target) {
        $target = $target -replace '^\\\\\?\?\\', ''
        if (-not [IO.Path]::IsPathRooted($target)) {
            $target = Join-Path $claudeDir $target
        }
        $resolvedTarget = [IO.Path]::GetFullPath($target)
        if ($resolvedTarget.TrimEnd('\', '/') -eq [IO.Path]::GetFullPath($source).TrimEnd('\', '/')) {
            Write-Host ".claude\skills already links to .agents\skills."
            return
        }
    }

    throw "$link is a link to '$target', not to $source. Remove it manually and rerun."
}

$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($onWindows) {
    New-Item -ItemType Junction -Path $link -Target $source | Out-Null
}
else {
    New-Item -ItemType SymbolicLink -Path $link -Target '../.agents/skills' | Out-Null
}

Write-Host "Linked .claude\skills -> .agents\skills."
