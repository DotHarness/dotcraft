[CmdletBinding()]
param(
    [string]$RegistryUrl = $env:DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL,
    [string]$MarketplacePath = ".craft/plugins/marketplace.json",
    [string]$CacheRoot,
    [switch]$ForceRefresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RefreshInterval = [TimeSpan]::FromHours(6)
$DownloadTimeout = [TimeSpan]::FromSeconds(2)

Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.Net.Http | Out-Null

function Normalize-RelativePath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $normalized = $PathValue.Replace('\', '/').Trim()
    if ($normalized.StartsWith("./", [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }

    return $normalized.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Test-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $normalized = $PathValue.Replace('\', '/').Trim()
    if ($normalized.StartsWith("./", [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }

    if ([string]::IsNullOrWhiteSpace($normalized) -or [System.IO.Path]::IsPathRooted($normalized)) {
        return $false
    }

    foreach ($segment in $normalized.Split([char[]]@('/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($segment -eq "..") {
            return $false
        }
    }

    return $true
}

function Test-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $pathFull = [System.IO.Path]::GetFullPath($PathValue).TrimEnd([char[]]@('\', '/'))
    $rootFull = [System.IO.Path]::GetFullPath($RootPath).TrimEnd([char[]]@('\', '/'))
    if ([string]::Equals($pathFull, $rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootWithSeparator = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    return $pathFull.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)
}

function Get-CacheBaseRoot {
    if (-not [string]::IsNullOrWhiteSpace($CacheRoot)) {
        return [System.IO.Path]::GetFullPath($CacheRoot.Trim())
    }

    $home = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ([string]::IsNullOrWhiteSpace($home)) {
        $home = [System.IO.Path]::GetTempPath()
    }

    return Join-Path $home ".craft\cache\plugin-registries"
}

function Get-DefaultRegistryUrl {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $desktopRuntimePath = Join-Path $repoRoot "desktop\src\main\ripgrepRuntime.ts"
    if (Test-Path -LiteralPath $desktopRuntimePath -PathType Leaf) {
        $text = Get-Content -LiteralPath $desktopRuntimePath -Raw
        $pattern = "DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL\s*=\s*(?:\r?\n\s*)?['`"]([^'`"]+)['`"]"
        $match = [regex]::Match($text, $pattern)
        if ($match.Success) {
            $value = $match.Groups[1].Value.Trim()
            $uri = $null
            if ([Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) `
                -and [string]::Equals($uri.Scheme, [Uri]::UriSchemeHttps, [StringComparison]::OrdinalIgnoreCase)) {
                return $value
            }
        }
    }

    throw "Default plugin registry URL is not configured. Set DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL or pass -RegistryUrl."
}

function Get-RegistryCacheRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceUrl,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue
    )

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($SourceUrl + "`n" + $MarketplacePathValue)
        $hash = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }

    $key = [BitConverter]::ToString($hash).Replace("-", "").ToLowerInvariant()
    return Join-Path (Get-CacheBaseRoot) $key
}

function Test-ShouldRefresh {
    param([Parameter(Mandatory = $true)][string]$SourceCacheRoot)

    $markerPath = Join-Path $SourceCacheRoot "updatedAt.txt"
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        return $true
    }

    $updatedAt = (Get-Item -LiteralPath $markerPath).LastWriteTimeUtc
    return ([DateTime]::UtcNow - $updatedAt) -gt $RefreshInterval
}

function Resolve-RegistryRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotRoot,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue,
        [Parameter(Mandatory = $true)][string]$SourceName
    )

    if (-not (Test-SafeRelativePath -PathValue $MarketplacePathValue)) {
        throw "Plugin registry marketplace path must be relative and stay within the registry snapshot: $MarketplacePathValue"
    }

    $snapshotRootFull = [System.IO.Path]::GetFullPath($SnapshotRoot)
    $relativeMarketplacePath = Normalize-RelativePath -PathValue $MarketplacePathValue
    $directMarketplacePath = Join-Path $snapshotRootFull $relativeMarketplacePath
    if (Test-Path -LiteralPath $directMarketplacePath -PathType Leaf) {
        return $snapshotRootFull
    }

    $children = @(Get-ChildItem -LiteralPath $snapshotRootFull -Directory)
    if ($children.Count -eq 1) {
        $nestedMarketplacePath = Join-Path $children[0].FullName $relativeMarketplacePath
        if (Test-Path -LiteralPath $nestedMarketplacePath -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($children[0].FullName)
        }
    }

    throw "Plugin registry source '$SourceName' does not contain marketplace '$MarketplacePathValue': $snapshotRootFull"
}

function Get-CachedSnapshotRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceUrl,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue
    )

    $sourceCacheRoot = Get-RegistryCacheRoot -SourceUrl $SourceUrl -MarketplacePathValue $MarketplacePathValue
    $snapshotRoot = Join-Path $sourceCacheRoot "snapshot"
    if (Test-Path -LiteralPath $snapshotRoot -PathType Container) {
        return $snapshotRoot
    }

    return $null
}

function Expand-ArchiveToCache {
    param(
        [Parameter(Mandatory = $true)][byte[]]$ArchiveBytes,
        [Parameter(Mandatory = $true)][string]$SourceUrl,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue
    )

    $sourceCacheRoot = Get-RegistryCacheRoot -SourceUrl $SourceUrl -MarketplacePathValue $MarketplacePathValue
    $cacheParent = [System.IO.Path]::GetDirectoryName($sourceCacheRoot)
    [System.IO.Directory]::CreateDirectory($cacheParent) | Out-Null

    $tempRoot = Join-Path $cacheParent ("." + [System.IO.Path]::GetFileName($sourceCacheRoot) + "." + [Guid]::NewGuid().ToString("N") + ".tmp")
    $tempSnapshotRoot = Join-Path $tempRoot "snapshot"

    try {
        [System.IO.Directory]::CreateDirectory($tempSnapshotRoot) | Out-Null
        $memoryStream = [System.IO.MemoryStream]::new($ArchiveBytes)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new($memoryStream, [System.IO.Compression.ZipArchiveMode]::Read)
            try {
                foreach ($entry in $archive.Entries) {
                    if ([string]::IsNullOrWhiteSpace($entry.FullName)) {
                        continue
                    }

                    $destination = [System.IO.Path]::GetFullPath((Join-Path $tempSnapshotRoot $entry.FullName))
                    if (-not (Test-PathWithin -PathValue $destination -RootPath $tempSnapshotRoot)) {
                        throw "Zip entry '$($entry.FullName)' escapes the registry snapshot root."
                    }

                    $isDirectory = [string]::IsNullOrEmpty($entry.Name) `
                        -or $entry.FullName.EndsWith("/", [StringComparison]::Ordinal) `
                        -or $entry.FullName.EndsWith("\", [StringComparison]::Ordinal)
                    if ($isDirectory) {
                        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
                        continue
                    }

                    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
                    $entryStream = $entry.Open()
                    try {
                        $fileStream = [System.IO.File]::Open(
                            $destination,
                            [System.IO.FileMode]::Create,
                            [System.IO.FileAccess]::Write,
                            [System.IO.FileShare]::None)
                        try {
                            $entryStream.CopyTo($fileStream)
                        } finally {
                            $fileStream.Dispose()
                        }
                    } finally {
                        $entryStream.Dispose()
                    }
                }
            } finally {
                $archive.Dispose()
            }
        } finally {
            $memoryStream.Dispose()
        }

        if (-not (Test-PathWithin -PathValue $sourceCacheRoot -RootPath $cacheParent)) {
            throw "Resolved cache root is outside the cache parent: $sourceCacheRoot"
        }

        if (Test-Path -LiteralPath $sourceCacheRoot) {
            Remove-Item -LiteralPath $sourceCacheRoot -Recurse -Force
        }

        Move-Item -LiteralPath $tempRoot -Destination $sourceCacheRoot | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $sourceCacheRoot "updatedAt.txt"),
            [DateTimeOffset]::UtcNow.ToString("O"))

        return Join-Path $sourceCacheRoot "snapshot"
    } finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

function Read-RemoteArchiveBytes {
    param([Parameter(Mandatory = $true)][Uri]$Uri)

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.Timeout = $DownloadTimeout
        $client.DefaultRequestHeaders.UserAgent.ParseAdd("DotCraft")
        $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) {
                throw "HTTP $([int]$response.StatusCode)"
            }

            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            return ,$bytes
        } finally {
            $response.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

function Resolve-PluginRegistryRoot {
    $sourceUrl = $RegistryUrl
    if ([string]::IsNullOrWhiteSpace($sourceUrl)) {
        $sourceUrl = Get-DefaultRegistryUrl
    }

    $sourceUrl = $sourceUrl.Trim()
    $sourceName = $sourceUrl

    if (Test-Path -LiteralPath $sourceUrl -PathType Container) {
        Write-Host "Using plugin registry directory: $sourceUrl" -ForegroundColor Gray
        return Resolve-RegistryRoot -SnapshotRoot $sourceUrl -MarketplacePathValue $MarketplacePath -SourceName $sourceName
    }

    if (Test-Path -LiteralPath $sourceUrl -PathType Leaf) {
        Write-Host "Extracting plugin registry archive: $sourceUrl" -ForegroundColor Gray
        try {
            $snapshotRoot = Expand-ArchiveToCache `
                -ArchiveBytes ([System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($sourceUrl))) `
                -SourceUrl ([System.IO.Path]::GetFullPath($sourceUrl)) `
                -MarketplacePathValue $MarketplacePath
            return Resolve-RegistryRoot -SnapshotRoot $snapshotRoot -MarketplacePathValue $MarketplacePath -SourceName $sourceName
        } catch {
            Write-Warning "Plugin registry archive could not be extracted: $($_.Exception.Message)"
            $cachedSnapshot = Get-CachedSnapshotRoot -SourceUrl ([System.IO.Path]::GetFullPath($sourceUrl)) -MarketplacePathValue $MarketplacePath
            if ($cachedSnapshot) {
                Write-Warning "Using cached plugin registry snapshot: $cachedSnapshot"
                return Resolve-RegistryRoot -SnapshotRoot $cachedSnapshot -MarketplacePathValue $MarketplacePath -SourceName $sourceName
            }

            throw
        }
    }

    $uri = $null
    if (-not [Uri]::TryCreate($sourceUrl, [UriKind]::Absolute, [ref]$uri) `
        -or -not [string]::Equals($uri.Scheme, [Uri]::UriSchemeHttps, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Plugin registry source must be an HTTPS archive URL or an existing local archive/directory path: $sourceUrl"
    }

    $sourceCacheRoot = Get-RegistryCacheRoot -SourceUrl $sourceUrl -MarketplacePathValue $MarketplacePath
    $cachedSnapshotRoot = Join-Path $sourceCacheRoot "snapshot"
    if ((Test-Path -LiteralPath $cachedSnapshotRoot -PathType Container) `
        -and -not $ForceRefresh `
        -and -not (Test-ShouldRefresh -SourceCacheRoot $sourceCacheRoot)) {
        Write-Host "Using cached plugin registry snapshot: $cachedSnapshotRoot" -ForegroundColor Gray
        return Resolve-RegistryRoot -SnapshotRoot $cachedSnapshotRoot -MarketplacePathValue $MarketplacePath -SourceName $sourceName
    }

    Write-Host "Downloading plugin registry: $sourceUrl" -ForegroundColor Gray
    try {
        $archiveBytes = Read-RemoteArchiveBytes -Uri $uri
        $snapshotRoot = Expand-ArchiveToCache -ArchiveBytes $archiveBytes -SourceUrl $sourceUrl -MarketplacePathValue $MarketplacePath
        return Resolve-RegistryRoot -SnapshotRoot $snapshotRoot -MarketplacePathValue $MarketplacePath -SourceName $sourceName
    } catch {
        Write-Warning "Plugin registry download failed: $($_.Exception.Message)"
        if (Test-Path -LiteralPath $cachedSnapshotRoot -PathType Container) {
            Write-Warning "Using cached plugin registry snapshot: $cachedSnapshotRoot"
            return Resolve-RegistryRoot -SnapshotRoot $cachedSnapshotRoot -MarketplacePathValue $MarketplacePath -SourceName $sourceName
        }

        throw "Failed to resolve plugin registry '$sourceUrl' and no cached snapshot is available."
    }
}

Write-Output (Resolve-PluginRegistryRoot)
