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
$StaleTemporaryDirectoryAge = [TimeSpan]::FromMinutes(10)
$CacheMetadataSchemaVersion = 1
$CacheMetadataFileName = "metadata.json"

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

    $userHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ([string]::IsNullOrWhiteSpace($userHome)) {
        $userHome = [System.IO.Path]::GetTempPath()
    }

    return Join-Path $userHome ".craft\cache\plugin-registries"
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

function Remove-RegistryCacheDirectoryBestEffort {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    try {
        if (Test-Path -LiteralPath $PathValue -PathType Container) {
            Remove-Item -LiteralPath $PathValue -Recurse -Force
        }
    } catch {
        Write-Warning "Plugin registry cache cleanup failed for '$PathValue': $($_.Exception.Message)"
    }
}

function Remove-StaleRegistryCacheTemporaryDirectories {
    param([DateTimeOffset]$Now = [DateTimeOffset]::UtcNow)

    $cacheBaseRoot = Get-CacheBaseRoot
    if (-not (Test-Path -LiteralPath $cacheBaseRoot -PathType Container)) {
        return
    }

    foreach ($directory in Get-ChildItem -LiteralPath $cacheBaseRoot -Directory -ErrorAction SilentlyContinue) {
        if ($directory.Name -notmatch '^\..+\.(tmp|stage|backup)$') {
            continue
        }

        $modified = [DateTimeOffset]::new($directory.LastWriteTimeUtc, [TimeSpan]::Zero)
        if (($Now - $modified) -ge $StaleTemporaryDirectoryAge) {
            Remove-RegistryCacheDirectoryBestEffort -PathValue $directory.FullName
        }
    }
}

function Read-MarketplaceIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotRoot,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue,
        [Parameter(Mandatory = $true)][string]$SourceName
    )

    $registryRoot = Resolve-RegistryRoot `
        -SnapshotRoot $SnapshotRoot `
        -MarketplacePathValue $MarketplacePathValue `
        -SourceName $SourceName
    $documentPath = Join-Path $registryRoot (Normalize-RelativePath -PathValue $MarketplacePathValue)
    try {
        $document = Get-Content -LiteralPath $documentPath -Raw | ConvertFrom-Json
    } catch {
        throw "Plugin registry marketplace document is invalid at '$documentPath': $($_.Exception.Message)"
    }

    $marketplaceName = [string]$document.name
    if ([string]::IsNullOrWhiteSpace($marketplaceName)) {
        throw "Plugin registry marketplace document must declare a name: $documentPath"
    }
    $marketplaceName = $marketplaceName.Trim()
    if ($marketplaceName -in @('.', '..') `
        -or $marketplaceName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 `
        -or $marketplaceName.Contains('/') `
        -or $marketplaceName.Contains('\')) {
        throw "Plugin registry marketplace name '$marketplaceName' is not a usable directory name: $documentPath"
    }

    return [pscustomobject]@{
        Name = $marketplaceName
        Root = $registryRoot
    }
}

function Get-RegistryCacheUpdatedAt {
    param([Parameter(Mandatory = $true)][string]$SourceCacheRoot)

    $markerPath = Join-Path $SourceCacheRoot "updatedAt.txt"
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        return [DateTimeOffset]::new((Get-Item -LiteralPath $markerPath).LastWriteTimeUtc, [TimeSpan]::Zero)
    }

    return [DateTimeOffset]::UtcNow
}

function Write-RegistryCacheMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$SourceCacheRoot,
        [Parameter(Mandatory = $true)][string]$MarketplaceName,
        [Parameter(Mandatory = $true)][string]$SourceKey,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue,
        [DateTimeOffset]$UpdatedAt = [DateTimeOffset]::UtcNow
    )

    $metadata = [ordered]@{
        schemaVersion = $CacheMetadataSchemaVersion
        marketplaceName = $MarketplaceName
        sourceKey = $SourceKey
        marketplacePath = $MarketplacePathValue
        updatedAt = $UpdatedAt.ToString("O")
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $SourceCacheRoot $CacheMetadataFileName),
        ($metadata | ConvertTo-Json))
}

function Get-RegistryCacheMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$SourceCacheRoot,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue
    )

    $metadataPath = Join-Path $SourceCacheRoot $CacheMetadataFileName
    if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
        try {
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            if ([int]$metadata.schemaVersion -eq $CacheMetadataSchemaVersion `
                -and -not [string]::IsNullOrWhiteSpace([string]$metadata.marketplaceName)) {
                return $metadata
            }
        } catch {
            # Fall through to legacy metadata reconstruction.
        }
    }

    $snapshotRoot = Join-Path $SourceCacheRoot "snapshot"
    if (-not (Test-Path -LiteralPath $snapshotRoot -PathType Container)) {
        return $null
    }

    try {
        $identity = Read-MarketplaceIdentity `
            -SnapshotRoot $snapshotRoot `
            -MarketplacePathValue $MarketplacePathValue `
            -SourceName $SourceCacheRoot
        $sourceKey = [System.IO.Path]::GetFileName($SourceCacheRoot)
        Write-RegistryCacheMetadata `
            -SourceCacheRoot $SourceCacheRoot `
            -MarketplaceName $identity.Name `
            -SourceKey $sourceKey `
            -MarketplacePathValue $MarketplacePathValue `
            -UpdatedAt (Get-RegistryCacheUpdatedAt -SourceCacheRoot $SourceCacheRoot)
        return Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Remove-OtherRegistryCacheVersions {
    param(
        [Parameter(Mandatory = $true)][string]$MarketplaceName,
        [string]$CurrentCacheRoot,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue
    )

    $cacheBaseRoot = Get-CacheBaseRoot
    if (-not (Test-Path -LiteralPath $cacheBaseRoot -PathType Container)) {
        return
    }

    $currentFullPath = if ([string]::IsNullOrWhiteSpace($CurrentCacheRoot)) {
        $null
    } else {
        [System.IO.Path]::GetFullPath($CurrentCacheRoot)
    }

    foreach ($directory in Get-ChildItem -LiteralPath $cacheBaseRoot -Directory -ErrorAction SilentlyContinue) {
        if ($directory.Name -match '^\..+\.(tmp|stage|backup)$' `
            -or ($currentFullPath -and [string]::Equals($directory.FullName, $currentFullPath, [StringComparison]::OrdinalIgnoreCase))) {
            continue
        }

        $metadata = Get-RegistryCacheMetadata `
            -SourceCacheRoot $directory.FullName `
            -MarketplacePathValue $MarketplacePathValue
        if ($null -eq $metadata `
            -and -not [string]::Equals($MarketplacePathValue, ".craft/plugins/marketplace.json", [StringComparison]::Ordinal)) {
            $metadata = Get-RegistryCacheMetadata `
                -SourceCacheRoot $directory.FullName `
                -MarketplacePathValue ".craft/plugins/marketplace.json"
        }

        if ($null -ne $metadata `
            -and [string]::Equals([string]$metadata.marketplaceName, $MarketplaceName, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-RegistryCacheDirectoryBestEffort -PathValue $directory.FullName
        }
    }
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

function Resolve-ArchiveRegistryRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotRoot,
        [Parameter(Mandatory = $true)][string]$SourceUrl,
        [Parameter(Mandatory = $true)][string]$MarketplacePathValue,
        [Parameter(Mandatory = $true)][string]$SourceName
    )

    $identity = Read-MarketplaceIdentity `
        -SnapshotRoot $SnapshotRoot `
        -MarketplacePathValue $MarketplacePathValue `
        -SourceName $SourceName
    $sourceCacheRoot = Get-RegistryCacheRoot `
        -SourceUrl $SourceUrl `
        -MarketplacePathValue $MarketplacePathValue
    $sourceKey = [System.IO.Path]::GetFileName($sourceCacheRoot)
    try {
        Write-RegistryCacheMetadata `
            -SourceCacheRoot $sourceCacheRoot `
            -MarketplaceName $identity.Name `
            -SourceKey $sourceKey `
            -MarketplacePathValue $MarketplacePathValue `
            -UpdatedAt (Get-RegistryCacheUpdatedAt -SourceCacheRoot $sourceCacheRoot)
    } catch {
        Write-Warning "Plugin registry cache metadata update failed for '$sourceCacheRoot': $($_.Exception.Message)"
    }

    Remove-OtherRegistryCacheVersions `
        -MarketplaceName $identity.Name `
        -CurrentCacheRoot $sourceCacheRoot `
        -MarketplacePathValue $MarketplacePathValue
    return $identity.Root
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
    Remove-StaleRegistryCacheTemporaryDirectories

    $sourceKey = [System.IO.Path]::GetFileName($sourceCacheRoot)
    $transactionKey = [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $tempRoot = Join-Path $cacheParent ("." + $transactionKey + ".stage")
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

        $identity = Read-MarketplaceIdentity `
            -SnapshotRoot $tempSnapshotRoot `
            -MarketplacePathValue $MarketplacePathValue `
            -SourceName $SourceUrl
        $activatedAt = [DateTimeOffset]::UtcNow
        Write-RegistryCacheMetadata `
            -SourceCacheRoot $tempRoot `
            -MarketplaceName $identity.Name `
            -SourceKey $sourceKey `
            -MarketplacePathValue $MarketplacePathValue `
            -UpdatedAt $activatedAt
        [System.IO.File]::WriteAllText(
            (Join-Path $tempRoot "updatedAt.txt"),
            $activatedAt.ToString("O"))

        $backupRoot = Join-Path $cacheParent ("." + $transactionKey + ".backup")
        $hasBackup = Test-Path -LiteralPath $sourceCacheRoot -PathType Container
        if ($hasBackup) {
            Move-Item -LiteralPath $sourceCacheRoot -Destination $backupRoot
        }

        try {
            Move-Item -LiteralPath $tempRoot -Destination $sourceCacheRoot
        } catch {
            if ($hasBackup `
                -and -not (Test-Path -LiteralPath $sourceCacheRoot) `
                -and (Test-Path -LiteralPath $backupRoot -PathType Container)) {
                Move-Item -LiteralPath $backupRoot -Destination $sourceCacheRoot
            }

            throw
        }

        if ($hasBackup) {
            Remove-RegistryCacheDirectoryBestEffort -PathValue $backupRoot
        }

        Remove-OtherRegistryCacheVersions `
            -MarketplaceName $identity.Name `
            -CurrentCacheRoot $sourceCacheRoot `
            -MarketplacePathValue $MarketplacePathValue

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
    Remove-StaleRegistryCacheTemporaryDirectories

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
        $archivePath = [System.IO.Path]::GetFullPath($sourceUrl)
        try {
            $snapshotRoot = Expand-ArchiveToCache `
                -ArchiveBytes ([System.IO.File]::ReadAllBytes($archivePath)) `
                -SourceUrl $archivePath `
                -MarketplacePathValue $MarketplacePath
            return Resolve-ArchiveRegistryRoot `
                -SnapshotRoot $snapshotRoot `
                -SourceUrl $archivePath `
                -MarketplacePathValue $MarketplacePath `
                -SourceName $sourceName
        } catch {
            Write-Warning "Plugin registry archive could not be extracted: $($_.Exception.Message)"
            $cachedSnapshot = Get-CachedSnapshotRoot -SourceUrl $archivePath -MarketplacePathValue $MarketplacePath
            if ($cachedSnapshot) {
                Write-Warning "Using cached plugin registry snapshot: $cachedSnapshot"
                return Resolve-ArchiveRegistryRoot `
                    -SnapshotRoot $cachedSnapshot `
                    -SourceUrl $archivePath `
                    -MarketplacePathValue $MarketplacePath `
                    -SourceName $sourceName
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
        return Resolve-ArchiveRegistryRoot `
            -SnapshotRoot $cachedSnapshotRoot `
            -SourceUrl $sourceUrl `
            -MarketplacePathValue $MarketplacePath `
            -SourceName $sourceName
    }

    Write-Host "Downloading plugin registry: $sourceUrl" -ForegroundColor Gray
    try {
        $archiveBytes = Read-RemoteArchiveBytes -Uri $uri
        $snapshotRoot = Expand-ArchiveToCache -ArchiveBytes $archiveBytes -SourceUrl $sourceUrl -MarketplacePathValue $MarketplacePath
        return Resolve-ArchiveRegistryRoot `
            -SnapshotRoot $snapshotRoot `
            -SourceUrl $sourceUrl `
            -MarketplacePathValue $MarketplacePath `
            -SourceName $sourceName
    } catch {
        Write-Warning "Plugin registry download failed: $($_.Exception.Message)"
        if (Test-Path -LiteralPath $cachedSnapshotRoot -PathType Container) {
            Write-Warning "Using cached plugin registry snapshot: $cachedSnapshotRoot"
            return Resolve-ArchiveRegistryRoot `
                -SnapshotRoot $cachedSnapshotRoot `
                -SourceUrl $sourceUrl `
                -MarketplacePathValue $MarketplacePath `
                -SourceName $sourceName
        }

        throw "Failed to resolve plugin registry '$sourceUrl' and no cached snapshot is available."
    }
}

Write-Output (Resolve-PluginRegistryRoot)
