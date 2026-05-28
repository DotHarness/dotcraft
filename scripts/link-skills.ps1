[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host ""
    Write-Host "================================" -ForegroundColor $Color
    Write-Host $Text -ForegroundColor $Color
    Write-Host "================================" -ForegroundColor $Color
}

function Resolve-LinkTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.LinkType) {
        return $null
    }

    $target = $item.Target
    if ($target -is [System.Array]) {
        $target = $target[0]
    }

    if ([string]::IsNullOrWhiteSpace($target)) {
        return $null
    }

    try {
        return [System.IO.Path]::GetFullPath($target)
    } catch {
        return $target
    }
}

function New-SkillsLink {
    param(
        [Parameter(Mandatory = $true)][string]$LinkPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    Write-Section -Text "Linking $DisplayName"
    Write-Host "Link path:   $LinkPath" -ForegroundColor Gray
    Write-Host "Target path: $TargetPath" -ForegroundColor Gray

    $parentDir = Split-Path -Parent $LinkPath
    if (-not (Test-Path -LiteralPath $parentDir)) {
        New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
        Write-Host "Created parent directory: $parentDir" -ForegroundColor Green
    }

    if (Test-Path -LiteralPath $LinkPath) {
        $existingItem = Get-Item -LiteralPath $LinkPath -Force
        $existingTarget = Resolve-LinkTarget -Path $LinkPath
        $expectedTarget = [System.IO.Path]::GetFullPath($TargetPath)

        if ($existingItem.LinkType -and $existingTarget -eq $expectedTarget) {
            Write-Host ""
            Write-Host "$DisplayName is already linked to the source skills." -ForegroundColor Green
            Write-Host "Nothing to change." -ForegroundColor Green
            return
        }

        if ($existingItem.LinkType) {
            Write-Host ""
            Write-Host "Replacing existing $($existingItem.LinkType) at $LinkPath" -ForegroundColor Yellow
            if ($existingTarget) {
                Write-Host "Current target: $existingTarget" -ForegroundColor Yellow
            }
            Remove-Item -LiteralPath $LinkPath -Force
        } else {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $leafName = Split-Path -Leaf $LinkPath
            $backupPath = Join-Path $parentDir ("$leafName.backup-" + $timestamp)

            Write-Host ""
            Write-Host "Existing $LinkPath is a normal directory. Moving it to backup:" -ForegroundColor Yellow
            Write-Host $backupPath -ForegroundColor Yellow
            Move-Item -LiteralPath $LinkPath -Destination $backupPath
        }
    }

    New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null

    $createdItem = Get-Item -LiteralPath $LinkPath -Force
    $createdTarget = Resolve-LinkTarget -Path $LinkPath

    Write-Host ""
    Write-Host "Created $($createdItem.LinkType) successfully." -ForegroundColor Green
    if ($createdTarget) {
        Write-Host "Target: $createdTarget" -ForegroundColor Green
    }
}

function New-PerSkillLinks {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    Write-Section -Text "Linking skills into $DisplayName"
    Write-Host "Source:      $SourceDir" -ForegroundColor Gray
    Write-Host "Destination: $DestinationDir" -ForegroundColor Gray

    if (-not (Test-Path -LiteralPath $DestinationDir)) {
        New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
        Write-Host "Created destination directory: $DestinationDir" -ForegroundColor Green
    } else {
        $destItem = Get-Item -LiteralPath $DestinationDir -Force
        if ($destItem.LinkType) {
            Write-Host ""
            Write-Host "Destination is a $($destItem.LinkType). Removing link and creating a real directory." -ForegroundColor Yellow
            $previousTarget = Resolve-LinkTarget -Path $DestinationDir
            if ($previousTarget) {
                Write-Host "Previous link target was: $previousTarget" -ForegroundColor Yellow
            }
            Remove-Item -LiteralPath $DestinationDir -Force
            New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
        }
    }

    $skillDirs = Get-ChildItem -LiteralPath $SourceDir -Directory
    if (-not $skillDirs -or $skillDirs.Count -eq 0) {
        Write-Host "No skill directories found under source. Skipping." -ForegroundColor Yellow
        return
    }

    $linkedCount = 0
    $skippedCount = 0
    foreach ($skill in $skillDirs) {
        $destSkillPath = Join-Path $DestinationDir $skill.Name
        $sourceSkillFull = [System.IO.Path]::GetFullPath($skill.FullName)

        if (Test-Path -LiteralPath $destSkillPath) {
            $existing = Get-Item -LiteralPath $destSkillPath -Force
            $existingTarget = Resolve-LinkTarget -Path $destSkillPath

            if ($existing.LinkType -and $existingTarget -eq $sourceSkillFull) {
                Write-Host "  - $($skill.Name): already linked, skipping" -ForegroundColor DarkGray
                $skippedCount++
                continue
            }

            if ($existing.LinkType) {
                Write-Host "  - $($skill.Name): replacing stale $($existing.LinkType)" -ForegroundColor Yellow
                Remove-Item -LiteralPath $destSkillPath -Force
            } else {
                Write-Host "  - $($skill.Name): removing existing directory (no backup, would pollute skills/)" -ForegroundColor Yellow
                Remove-Item -LiteralPath $destSkillPath -Recurse -Force
            }
        } else {
            Write-Host "  - $($skill.Name): linking" -ForegroundColor Green
        }

        New-Item -ItemType Junction -Path $destSkillPath -Target $sourceSkillFull | Out-Null
        $linkedCount++
    }

    Write-Host ""
    Write-Host "Linked $linkedCount skill(s), $skippedCount already up to date." -ForegroundColor Green
    Write-Host "Other skills already present in $DisplayName were left untouched." -ForegroundColor Green
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$samplesSkillsPath = Join-Path $repoRoot "samples\plugins\dotcraft-dev\skills"
$cursorSkillsPath = Join-Path $repoRoot ".cursor\skills"
$codexSkillsPath = Join-Path $env:USERPROFILE ".codex\skills"

Write-Section -Text "DotCraft Skills Linker"
Write-Host "Repository root: $repoRoot" -ForegroundColor Gray
Write-Host "Source skills:   $samplesSkillsPath" -ForegroundColor Gray

if (-not (Test-Path -LiteralPath $samplesSkillsPath)) {
    throw "Source skills directory not found: $samplesSkillsPath"
}

New-SkillsLink -LinkPath $cursorSkillsPath -TargetPath $samplesSkillsPath -DisplayName ".cursor\skills"
New-PerSkillLinks -SourceDir $samplesSkillsPath -DestinationDir $codexSkillsPath -DisplayName "~\.codex\skills"

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  - Cursor reads skills via a junction on .cursor\skills." -ForegroundColor Green
Write-Host "  - Codex gets per-skill junctions; its own skills are preserved." -ForegroundColor Green
Write-Host "Skill edits in the repo take effect immediately on both sides." -ForegroundColor Green
