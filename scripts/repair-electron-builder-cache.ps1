param(
    [int]$MaxRetries = 5,
    [int]$InitialRetryDelayMilliseconds = 250
)

$ErrorActionPreference = "Stop"

function Get-CacheRoot {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not set; cannot locate the electron-builder cache."
    }

    Join-Path $env:LOCALAPPDATA "electron-builder\Cache\$Name"
}

function Get-ChildItemCount {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 0
    }

    @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop).Count
}

function Remove-PathWithRetry {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $delay = $InitialRetryDelayMilliseconds
    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            Write-Host "Removed electron-builder cache artifact: $Path"
            return
        }
        catch {
            if ($attempt -eq $MaxRetries) {
                throw "Failed to remove '$Path' after $MaxRetries attempts. Close any running build/npm/electron-builder process, or temporarily allow-list the electron-builder cache in security software, then retry. Last error: $($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds $delay
            $delay = [Math]::Min($delay * 2, 2000)
        }
    }
}

function Test-RelevantBuilderProcess {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $desktopRoot = Join-Path $repoRoot "desktop"
    $escapedDesktopRoot = [regex]::Escape($desktopRoot)

    $currentPid = $PID
    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessId -ne $currentPid -and
            $_.CommandLine -and
            (
                $_.CommandLine -match "electron-builder" -or
                $_.CommandLine -match "app-builder" -or
                ($_.CommandLine -match $escapedDesktopRoot -and $_.CommandLine -match "electron-vite\s+build")
            )
        }

    return @($processes | Where-Object { $_ -ne $null })
}

function Repair-CacheRoot {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        Write-Host "electron-builder cache root not found, skipping: $Root"
        return
    }

    $stateFiles = @(Get-ChildItem -LiteralPath $Root -Force -File -Filter "*.state" -ErrorAction Stop)
    $candidateNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($stateFile in $stateFiles) {
        [void]$candidateNames.Add($stateFile.BaseName)
    }

    foreach ($tmpDir in @(Get-ChildItem -LiteralPath $Root -Force -Directory -Filter "*.tmp" -ErrorAction Stop)) {
        [void]$candidateNames.Add([System.IO.Path]::GetFileNameWithoutExtension($tmpDir.Name))
    }

    foreach ($dir in @(Get-ChildItem -LiteralPath $Root -Force -Directory -ErrorAction Stop | Where-Object { -not $_.Name.EndsWith(".tmp", [StringComparison]::OrdinalIgnoreCase) })) {
        if ((Get-ChildItemCount -Path $dir.FullName) -eq 0) {
            [void]$candidateNames.Add($dir.Name)
        }
    }

    foreach ($name in $candidateNames) {
        $finalPath = Join-Path $Root $name
        $statePath = Join-Path $Root "$name.state"
        $tmpPath = Join-Path $Root "$name.tmp"

        $state = $null
        if (Test-Path -LiteralPath $statePath -PathType Leaf) {
            try {
                $state = (Get-Content -LiteralPath $statePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop).state
            }
            catch {
                Write-Host "Invalid electron-builder cache state file will be repaired: $statePath"
                $state = "invalid"
            }
        }

        $hasIncompleteState = $state -and $state -ne "complete"
        $finalMissing = -not (Test-Path -LiteralPath $finalPath -PathType Container)
        $finalEmpty = (Test-Path -LiteralPath $finalPath -PathType Container) -and ((Get-ChildItemCount -Path $finalPath) -eq 0)
        $tmpExists = Test-Path -LiteralPath $tmpPath

        if ($hasIncompleteState -or $finalMissing -or $finalEmpty -or $tmpExists) {
            Write-Host "Repairing electron-builder cache entry: $name"
            Remove-PathWithRetry -Path $tmpPath
            Remove-PathWithRetry -Path $finalPath
            Remove-PathWithRetry -Path $statePath
        }
    }
}

$runningBuilders = Test-RelevantBuilderProcess
if ($runningBuilders.Count -gt 0) {
    Write-Error "Detected another electron-builder/electron-vite build process. Close it before repairing the cache: $($runningBuilders.ProcessId -join ', ')"
    exit 1
}

$cacheRoots = @()
$cacheRoots += Get-CacheRoot -Name "7zip@1.0.0"
$cacheRoots += Get-CacheRoot -Name "nsis-3.0.4.1"

foreach ($root in $cacheRoots) {
    Repair-CacheRoot -Root $root
}

Write-Host "electron-builder cache repair completed."
