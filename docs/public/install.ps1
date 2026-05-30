$ErrorActionPreference = "Stop"

$Repo = if ($env:DOTCRAFT_REPO) { $env:DOTCRAFT_REPO } else { "DotHarness/dotcraft" }
$InstallDir = if ($env:DOTCRAFT_INSTALL_DIR) { $env:DOTCRAFT_INSTALL_DIR } else { Join-Path $HOME ".craft\bin" }
$Version = if ($env:DOTCRAFT_VERSION) { $env:DOTCRAFT_VERSION } else { "latest" }

$processorArch = $env:PROCESSOR_ARCHITECTURE
$wow64Arch = $env:PROCESSOR_ARCHITEW6432
if (($processorArch -ne "AMD64") -and ($wow64Arch -ne "AMD64")) {
    throw "Unsupported architecture. DotCraft CLI releases are currently x64-only."
}

if ($Version -eq "latest") {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ "User-Agent" = "dotcraft-install" }
    $Version = $release.tag_name
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Could not resolve DotCraft version."
}

$archive = "DotCraft-$Version-win-x64.zip"
$url = "https://github.com/$Repo/releases/download/$Version/$archive"
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
