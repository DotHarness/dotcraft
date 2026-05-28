$ErrorActionPreference = "Stop"

$pluginRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$version = if ($env:CSHARP_LS_VERSION) { $env:CSHARP_LS_VERSION } else { "0.24.0" }
$toolPath = Join-Path $pluginRoot "server\csharp-ls"

New-Item -ItemType Directory -Force -Path $toolPath | Out-Null

$shimNames = @("csharp-ls.exe", "csharp-ls")
$hasShim = $false
foreach ($shimName in $shimNames) {
    if (Test-Path (Join-Path $toolPath $shimName)) {
        $hasShim = $true
        break
    }
}

if ($hasShim) {
    dotnet tool update csharp-ls --tool-path $toolPath --version $version
} else {
    dotnet tool install csharp-ls --tool-path $toolPath --version $version
}

Write-Host "csharp-ls $version is installed under $toolPath"
