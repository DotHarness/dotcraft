$ErrorActionPreference = "Stop"

$DefaultRepo = "DotHarness/dotcraft"
$Repo = if ($env:DOTCRAFT_REPO) { $env:DOTCRAFT_REPO } else { $DefaultRepo }
$InstallDir = if ($env:DOTCRAFT_INSTALL_DIR) { $env:DOTCRAFT_INSTALL_DIR } else { Join-Path $HOME ".craft\bin" }
$Version = if ($env:DOTCRAFT_VERSION) { $env:DOTCRAFT_VERSION } else { "latest" }
$ManifestUrl = "https://www.dotcraft.net/release-downloads.json"

function Get-DotCraftWindowsArch {
    $runtimeArch = $null
    try {
        $runtimeArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    catch {
        $runtimeArch = $null
    }

    $candidates = @(
        $runtimeArch,
        $env:PROCESSOR_ARCHITEW6432,
        $env:PROCESSOR_ARCHITECTURE,
        $env:PROCESSOR_IDENTIFIER
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if ($candidates | Where-Object { $_ -match "ARM64|AARCH64|ARMv8" }) {
        return "arm64"
    }

    if ($candidates | Where-Object { $_ -match "AMD64|X64" }) {
        return "x64"
    }

    $detected = if ($candidates.Count -gt 0) { $candidates -join ", " } else { "unknown" }
    throw "Unsupported architecture: $detected. DotCraft CLI releases are available for Windows x64 and arm64."
}

$Arch = Get-DotCraftWindowsArch
$archive = $null
$url = $null

if ($Version -eq "latest") {
    if ($Repo -eq $DefaultRepo) {
        $manifest = Invoke-RestMethod -Uri $ManifestUrl -Headers @{ "User-Agent" = "dotcraft-install" }
        $Version = $manifest.tag
        $assetId = "cli-win-$Arch"
        $asset = $manifest.assets.PSObject.Properties[$assetId].Value
        if ($null -eq $asset) {
            throw "Release manifest does not contain $assetId."
        }
        $archive = $asset.fileName
        $url = $asset.url
    }
    else {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ "User-Agent" = "dotcraft-install" }
        $Version = $release.tag_name
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Could not resolve DotCraft version."
}

if ([string]::IsNullOrWhiteSpace($url)) {
    $archive = "DotCraft-$Version-win-$Arch.zip"
    $url = "https://github.com/$Repo/releases/download/$Version/$archive"
}
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("dotcraft-install-" + [Guid]::NewGuid().ToString("N"))
$zip = Join-Path $tmp $archive

New-Item -ItemType Directory -Force -Path $tmp | Out-Null
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

try {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -Headers @{ "User-Agent" = "dotcraft-install" }
    Expand-Archive -Path $zip -DestinationPath $InstallDir -Force
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

$currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
$pathParts = @()
if ($currentUserPath) {
    $pathParts = $currentUserPath -split ";" | Where-Object { $_ }
}

if ($pathParts -notcontains $InstallDir) {
    $nextPath = if ($currentUserPath) { "$currentUserPath;$InstallDir" } else { $InstallDir }
    [Environment]::SetEnvironmentVariable("Path", $nextPath, "User")
    $env:Path = "$env:Path;$InstallDir"
    Write-Host "Added $InstallDir to the user PATH. Open a new terminal if the command is not found."
}

Write-Host "DotCraft $Version installed to $InstallDir"
$dotcraft = Join-Path $InstallDir "dotcraft.exe"
if (Test-Path $dotcraft) {
    & $dotcraft --version
}
