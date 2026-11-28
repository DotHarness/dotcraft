[CmdletBinding()]
param(
    [string]$PluginsRepoRoot = $env:DOTCRAFT_PLUGINS_REPO,
    [string]$PluginRegistryUrl = $env:DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL,
    [switch]$ForcePluginRegistryRefresh,
    [string]$DestinationDir
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

function Select-LinkDestinations {
    param([Parameter(Mandatory = $true)][object[]]$Destinations)

    $activeIndex = 0
    $selected = @($Destinations | ForEach-Object { $false })
    $lineWidth = [Console]::WindowWidth - 1
    $cursorVisible = [Console]::CursorVisible
    [Console]::CursorVisible = $false
    try {
        foreach ($destination in $Destinations) {
            [Console]::WriteLine()
        }
        $listTop = [Console]::CursorTop - $Destinations.Count

        while ($true) {
            for ($i = 0; $i -lt $Destinations.Count; $i++) {
                [Console]::SetCursorPosition(0, $listTop + $i)
                $pointer = if ($i -eq $activeIndex) { ">" } else { " " }
                $mark = if ($selected[$i]) { "[x]" } else { "[ ]" }
                $line = "$pointer $mark $($Destinations[$i].Name)"
                [Console]::Write($line.PadRight($lineWidth))
            }

            switch ([Console]::ReadKey($true).Key) {
                "UpArrow" { $activeIndex = ($activeIndex - 1 + $Destinations.Count) % $Destinations.Count }
                "DownArrow" { $activeIndex = ($activeIndex + 1) % $Destinations.Count }
                "Spacebar" { $selected[$activeIndex] = -not $selected[$activeIndex] }
                "Enter" {
                    $result = @(for ($i = 0; $i -lt $Destinations.Count; $i++) {
                        if ($selected[$i]) { $Destinations[$i] }
                    })
                    if ($result.Count -gt 0) {
                        [Console]::SetCursorPosition(0, $listTop + $Destinations.Count)
                        return $result
                    }
                }
            }
        }
    } finally {
        [Console]::CursorVisible = $cursorVisible
    }
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

        $existing = Get-Item -LiteralPath $destSkillPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $existing) {
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

function Format-AntigravityManifestPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/')).Replace('\', '/')
}

function Update-AntigravitySkillsManifest {
    param(
        [Parameter(Mandatory = $true)][object[]]$SkillSources,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    Write-Section -Text "Registering skills in $DisplayName"
    Write-Host "Manifest: $ManifestPath" -ForegroundColor Gray

    $manifestDir = Split-Path -Parent $ManifestPath
    if (-not (Test-Path -LiteralPath $manifestDir)) {
        New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
    }

    $manifest = [ordered]@{}
    $existingEntries = @()
    if (Test-Path -LiteralPath $ManifestPath -PathType Leaf) {
        $raw = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8)
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $parsed = $raw | ConvertFrom-Json
            foreach ($prop in $parsed.PSObject.Properties) {
                if ($prop.Name -eq "entries") {
                    if ($null -ne $prop.Value) {
                        $existingEntries = @($prop.Value)
                    }
                } else {
                    $manifest[$prop.Name] = $prop.Value
                }
            }
        }
    }

    $normalizedSources = @($SkillSources | ForEach-Object {
        @{
            Name = $_.Name
            Path = Format-AntigravityManifestPath -Path $_.Path
        }
    })
    $targetPathKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($source in $normalizedSources) {
        [void]$targetPathKeys.Add($source.Path)
    }

    $seenTargetKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $mergedEntries = New-Object 'System.Collections.Generic.List[object]'
    $removedCount = 0
    foreach ($entry in $existingEntries) {
        $pathProp = if ($null -ne $entry) { $entry.PSObject.Properties['path'] } else { $null }
        if ($null -eq $pathProp -or [string]::IsNullOrWhiteSpace([string]$pathProp.Value)) {
            $mergedEntries.Add($entry)
            continue
        }

        $entryPath = ([string]$pathProp.Value).Trim().Replace('\', '/').TrimEnd('/')
        if ($targetPathKeys.Contains($entryPath)) {
            if ($seenTargetKeys.Add($entryPath)) {
                $mergedEntries.Add($entry)
            }
            continue
        }

        if ($entryPath -match '/\.craft/cache/(?:plugin-registries|link-skills-registries)/[^/]+/snapshot(?:/[^/]+)?/plugins/dotharness/skills$') {
            Write-Host "  - removed stale DotHarness registry entry: $entryPath" -ForegroundColor Yellow
            $removedCount++
            continue
        }

        $mergedEntries.Add($entry)
    }

    $addedCount = 0
    $skippedCount = 0
    foreach ($source in $normalizedSources) {
        if ($seenTargetKeys.Contains($source.Path)) {
            Write-Host "  - $($source.Name): already registered, skipping" -ForegroundColor DarkGray
            $skippedCount++
            continue
        }

        $mergedEntries.Add([ordered]@{ path = $source.Path })
        Write-Host "  - $($source.Name): added $($source.Path)" -ForegroundColor Green
        $addedCount++
    }

    $manifest['entries'] = @($mergedEntries.ToArray())
    $json = ($manifest | ConvertTo-Json -Depth 10) + "`n"
    [System.IO.File]::WriteAllText($ManifestPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Host ""
    Write-Host "Registered $addedCount source(s), $skippedCount already up to date, $removedCount stale entry(s) removed." -ForegroundColor Green
    Write-Host "Other entries already present in $DisplayName were left untouched." -ForegroundColor Green
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
$repositorySkillsPath = Join-Path $repoRoot ".agents\skills"
$dotHarnessSkillsPath = Join-Path $PluginsRepoRoot "plugins\dotharness\skills"
$registrySkillSourceNames = @("DotHarness skills")
$skillSources = @(
    @{
        Name = "DotCraft bundled skills"
        Path = $dotcraftSkillsPath
    },
    @{
        Name = "DotCraft repository skills"
        Path = $repositorySkillsPath
    },
    @{
        Name = "DotHarness skills"
        Path = $dotHarnessSkillsPath
    }
)
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

$availableDestinations = @(
    @{
        Name = "Cursor (.cursor\skills)"
        Path = Join-Path $repoRoot ".cursor\skills"
        Mode = "Junction"
    },
    @{
        Name = "Claude (~\.claude\skills)"
        Path = Join-Path $env:USERPROFILE ".claude\skills"
        Mode = "Junction"
    },
    @{
        Name = "Codex (~\.codex\skills)"
        Path = Join-Path $env:USERPROFILE ".codex\skills"
        Mode = "Junction"
    },
    @{
        Name = "Antigravity (~\.gemini\config\skills.json)"
        Path = Join-Path $env:USERPROFILE ".gemini\config\skills.json"
        Mode = "AntigravityJson"
    }
)
$skillDestinations = if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    @(Select-LinkDestinations -Destinations $availableDestinations)
} else {
    $resolvedDestination = [System.IO.Path]::GetFullPath($DestinationDir)
    $destinationMode = if ([System.IO.Path]::GetFileName($resolvedDestination) -ieq "skills.json") {
        "AntigravityJson"
    } else {
        "Junction"
    }
    @(@{
        Name = "Custom destination"
        Path = $resolvedDestination
        Mode = $destinationMode
    })
}
$legacyDotCraftSkillNames = @("dev-guide", "docs-guide", "release-draft")

foreach ($skillDestination in $skillDestinations) {
    if ($skillDestination.Mode -eq "AntigravityJson") {
        Update-AntigravitySkillsManifest -SkillSources $skillSources -ManifestPath $skillDestination.Path -DisplayName $skillDestination.Name
        continue
    }

    foreach ($legacyName in $legacyDotCraftSkillNames) {
        Remove-LegacyDotCraftSkillLink -DestinationDir $skillDestination.Path -LegacyName $legacyName
    }

    foreach ($skillSource in $skillSources) {
        New-PerSkillLinks -SourceDir $skillSource.Path -DestinationDir $skillDestination.Path -DisplayName $skillDestination.Name
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Folder destinations get per-skill junctions and Antigravity registers source directories in skills.json; unrelated existing skills are left untouched." -ForegroundColor Green
Write-Host "DotCraft skill edits in this repo take effect immediately in the selected agents." -ForegroundColor Green
Write-Host "DotHarness skills come from the local override or resolved plugin registry; use -ForcePluginRegistryRefresh to refresh the registry now." -ForegroundColor Green
