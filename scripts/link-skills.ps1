[CmdletBinding()]
param(
    [string]$PluginsRepoRoot = $env:DOTCRAFT_PLUGINS_REPO,
    [string]$PluginRegistryUrl = $env:DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL,
    [switch]$ForcePluginRegistryRefresh
)

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

function Remove-LegacyDotCraftSkillLink {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [Parameter(Mandatory = $true)][string]$LegacyName
    )

    $legacyPath = Join-Path $DestinationDir $LegacyName
    $item = Get-Item -LiteralPath $legacyPath -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or -not $item.LinkType) {
        return
    }

    $target = Resolve-LinkTarget -Path $legacyPath
    if ([string]::IsNullOrWhiteSpace($target)) {
        return
    }

    $normalizedTarget = $target.Replace('/', '\')
    $legacySuffix = "\plugins\dotcraft-dev\skills\$LegacyName"
    if (-not $normalizedTarget.EndsWith($legacySuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    Remove-Item -LiteralPath $legacyPath -Force
    Write-Host "  - ${LegacyName}: removed legacy DotCraft dev skill link" -ForegroundColor Yellow
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

    $skillDirs = @(Get-ChildItem -LiteralPath $SourceDir -Directory)
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
$pluginRegistrySource = "local override"
if ([string]::IsNullOrWhiteSpace($PluginsRepoRoot)) {
    $resolverPath = Join-Path $PSScriptRoot "resolve-plugin-registry.ps1"
    if (-not (Test-Path -LiteralPath $resolverPath -PathType Leaf)) {
        throw "Plugin registry resolver not found: $resolverPath"
    }

    $resolverArgs = @{
        MarketplacePath = ".craft/plugins/marketplace.json"
    }
    if (-not [string]::IsNullOrWhiteSpace($PluginRegistryUrl)) {
        $resolverArgs.RegistryUrl = $PluginRegistryUrl
    }
    if ($ForcePluginRegistryRefresh) {
        $resolverArgs.ForceRefresh = $true
    }

    $resolvedRegistryRoots = @(& $resolverPath @resolverArgs)
    $PluginsRepoRoot = [string]($resolvedRegistryRoots | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($PluginsRepoRoot)) {
        throw "Plugin registry resolver did not return a registry root."
    }

    $pluginRegistrySource = "resolved registry"
}

$dotcraftSkillsPath = Join-Path $repoRoot "desktop\resources\plugins\dotcraft-bundled\plugins\dotcraft\skills"
$harnessWorkflowSkillsPath = Join-Path $PluginsRepoRoot "plugins\harness-workflow\skills"
$registrySkillSourceNames = @("Harness Workflow skills")
$skillSources = @(
    @{
        Name = "DotCraft skills"
        Path = $dotcraftSkillsPath
    },
    @{
        Name = "Harness Workflow skills"
        Path = $harnessWorkflowSkillsPath
    },
    @{
        Name = "DotCraft Doctor skills"
        Path = Join-Path $repoRoot "desktop\resources\plugins\dotcraft-bundled\plugins\dotcraft-doctor\skills"
    }
)
$cursorSkillsPath = Join-Path $repoRoot ".cursor\skills"
$codexSkillsPath = Join-Path $env:USERPROFILE ".codex\skills"
$claudeSkillsPath = Join-Path $env:USERPROFILE ".claude\skills"

Write-Section -Text "DotCraft Skills Linker"
Write-Host "Repository root: $repoRoot" -ForegroundColor Gray
Write-Host "Plugin registry source: $pluginRegistrySource" -ForegroundColor Gray
Write-Host "Plugin registry root:   $PluginsRepoRoot" -ForegroundColor Gray
Write-Host "Source skills:" -ForegroundColor Gray
foreach ($skillSource in $skillSources) {
    Write-Host "  - $($skillSource.Name): $($skillSource.Path)" -ForegroundColor Gray
}

foreach ($skillSource in $skillSources) {
    if (-not (Test-Path -LiteralPath $skillSource.Path)) {
        if ($registrySkillSourceNames -contains $skillSource.Name) {
            throw "Source skills directory not found: $($skillSource.Path). Pass -PluginsRepoRoot or set DOTCRAFT_PLUGINS_REPO for a local checkout, or pass -PluginRegistryUrl / set DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL for another registry."
        }

        throw "Source skills directory not found: $($skillSource.Path)"
    }
}

$skillDestinations = @(
    @{
        Name = ".cursor\skills"
        Path = $cursorSkillsPath
    },
    @{
        Name = "~\.codex\skills"
        Path = $codexSkillsPath
    },
    @{
        Name = "~\.claude\skills"
        Path = $claudeSkillsPath
    }
)
$legacyDotCraftSkillNames = @("dev-guide", "docs-guide", "release-draft")

foreach ($skillDestination in $skillDestinations) {
    foreach ($legacyName in $legacyDotCraftSkillNames) {
        Remove-LegacyDotCraftSkillLink -DestinationDir $skillDestination.Path -LegacyName $legacyName
    }

    foreach ($skillSource in $skillSources) {
        New-PerSkillLinks -SourceDir $skillSource.Path -DestinationDir $skillDestination.Path -DisplayName $skillDestination.Name
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  - Cursor gets per-skill junctions; unrelated existing skills are left untouched." -ForegroundColor Green
Write-Host "  - Codex gets per-skill junctions; unrelated existing skills are left untouched." -ForegroundColor Green
Write-Host "  - Claude gets per-skill junctions; unrelated existing skills are left untouched." -ForegroundColor Green
Write-Host "DotCraft and DotCraft Doctor skill edits in this repo take effect immediately in all linked tools." -ForegroundColor Green
Write-Host "Harness Workflow skills come from the local override or resolved plugin registry; use -ForcePluginRegistryRefresh to refresh the registry now." -ForegroundColor Green
