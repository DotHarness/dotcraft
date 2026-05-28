$ErrorActionPreference = "Stop"

$pluginRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$version = if ($env:CLANGD_VERSION) { $env:CLANGD_VERSION } else { "22.1.0" }
$platform = if ($env:CLANGD_PLATFORM) {
    $env:CLANGD_PLATFORM
} elseif ($env:OS -eq "Windows_NT" -or $IsWindows) {
    "windows"
} elseif ($IsMacOS) {
    "mac"
} else {
    "linux"
}

$archiveName = "clangd-$platform-$version.zip"
$url = if ($env:CLANGD_URL) {
    $env:CLANGD_URL
} else {
    "https://github.com/clangd/clangd/releases/download/$version/$archiveName"
}

$serverRoot = Join-Path $pluginRoot "server\clangd"
$stageRoot = Join-Path $serverRoot "_extract"
$archivePath = Join-Path $serverRoot $archiveName

New-Item -ItemType Directory -Force -Path $serverRoot | Out-Null
if (Test-Path $stageRoot) {
    Remove-Item -Recurse -Force -LiteralPath $stageRoot
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

Invoke-WebRequest -Uri $url -OutFile $archivePath
Expand-Archive -Path $archivePath -DestinationPath $stageRoot -Force

$executableName = if ($platform -eq "windows") { "clangd.exe" } else { "clangd" }
$clangd = Get-ChildItem -Path $stageRoot -Recurse -File -Filter $executableName |
    Where-Object { $_.FullName -match "[\\/](bin)[\\/]" } |
    Select-Object -First 1

if (-not $clangd) {
    throw "Could not find bin/$executableName in $archiveName"
}

$payloadRoot = Split-Path -Parent (Split-Path -Parent $clangd.FullName)
Get-ChildItem -Path $payloadRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $serverRoot -Recurse -Force
}

Remove-Item -Recurse -Force -LiteralPath $stageRoot
Remove-Item -Force -LiteralPath $archivePath

Write-Host "clangd $version is installed under $serverRoot"
