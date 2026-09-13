param(
    [string]$PackagePath,
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) "dotcraft-harness-package-probe-$([guid]::NewGuid().ToString('N'))"
$probeRoot = [IO.Path]::GetFullPath($probeRoot)
New-Item -ItemType Directory -Path $probeRoot | Out-Null
$completed = $false
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$previousBundleRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Copy-Fixture {
    param([string]$Name)
    $destination = Join-Path $probeRoot $Name
    New-Item -ItemType Directory -Path $destination | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "tests/$Name") -File |
        Where-Object { $_.Extension -in '.cs', '.csproj' } |
        Copy-Item -Destination $destination
    return Join-Path $destination "$Name.csproj"
}

function Get-EntryHash {
    param($Entry)
    $stream = $Entry.Open()
    try {
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-AuthoringReferencePack {
    param([string]$HostDirectory)
    foreach ($relativePath in @('DotCraft.Core.dll', 'DotCraft.Core.xml', 'DotCraft.Agents.dll',
        'DotCraft.Agents.xml', 'DotCraft.Generators.dll', 'refs/System.Runtime.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $HostDirectory $relativePath) -PathType Leaf)) {
            throw "The actual host output is missing authoring reference-pack file $relativePath in $HostDirectory."
        }
    }
}

try {
    $sourceRoot = Join-Path $probeRoot 'source'
    New-Item -ItemType Directory -Path $sourceRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        Invoke-Dotnet -Arguments @('pack', (Join-Path $repoRoot 'src/DotCraft.Harness/DotCraft.Harness.csproj'),
            '--configuration', $Configuration, '--output', $sourceRoot)
        $packages = @(Get-ChildItem -LiteralPath $sourceRoot -Filter 'DotCraft.Harness.*.nupkg')
        if ($packages.Count -ne 1) { throw 'Expected exactly one freshly packed Harness candidate.' }
        $PackagePath = $packages[0].FullName
    }
    else {
        $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
        Copy-Item -LiteralPath $PackagePath -Destination $sourceRoot
    }

    $package = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $runtimeGenerator = $package.GetEntry('lib/net10.0/DotCraft.Generators.dll')
        $analyzerGenerator = $package.GetEntry('analyzers/dotnet/cs/DotCraft.Generators.dll')
        if (!$runtimeGenerator -or !$analyzerGenerator) { throw 'Candidate must contain the runtime generator and analyzer.' }
        if ((Get-EntryHash $runtimeGenerator) -cne (Get-EntryHash $analyzerGenerator)) {
            throw 'Candidate runtime generator and analyzer are not the same binary.'
        }
        $nuspecs = @($package.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
        if ($nuspecs.Count -ne 1) { throw 'Expected one candidate nuspec.' }
        $reader = [IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml]$nuspec = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $packageVersion = $nuspec.package.metadata.version
        if ($nuspec.package.metadata.id -cne 'DotCraft.Harness') { throw 'Pass a DotCraft.Harness candidate package.' }
    }
    finally { $package.Dispose() }

    $env:NUGET_PACKAGES = Join-Path $probeRoot 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $probeRoot 'http-cache'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $probeRoot 'bundle-extract'
    $nugetConfig = Join-Path $probeRoot 'NuGet.config'
    $escapedSource = [Security.SecurityElement]::Escape($sourceRoot)
    [IO.File]::WriteAllText($nugetConfig, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="candidate"><package pattern="DotCraft.Harness" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@)

    $pluginProject = Copy-Fixture 'DotCraft.Harness.PluginFixture'
    $consumerProject = Copy-Fixture 'DotCraft.Harness.Consumer'
    $packageProperties = @('-p:UseHarnessPackage=true', "-p:HarnessPackageVersion=$packageVersion")
    $pluginOutput = Join-Path $probeRoot 'plugin-output'
    $consumerOutput = Join-Path $probeRoot 'consumer-output'
    foreach ($project in @($pluginProject, $consumerProject)) {
        Invoke-Dotnet (@('restore', $project, '--configfile', $nugetConfig, '--packages', $env:NUGET_PACKAGES) + $packageProperties)
    }
    Invoke-Dotnet (@('build', $pluginProject, '--configuration', $Configuration, '--no-restore', '--output', $pluginOutput) + $packageProperties)
    Invoke-Dotnet (@('build', $consumerProject, '--configuration', $Configuration, '--no-restore', '--output', $consumerOutput) + $packageProperties)
    Assert-AuthoringReferencePack $consumerOutput
    Invoke-Dotnet -Arguments @((Join-Path $consumerOutput 'DotCraft.Harness.Consumer.dll'), $pluginOutput)

    if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
        $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        $RuntimeIdentifier = "$os-$architecture"
    }
    $singleFileOutput = Join-Path $probeRoot 'single-file-output'
    Invoke-Dotnet (@('publish', $consumerProject, '--configuration', $Configuration,
        '--runtime', $RuntimeIdentifier, '--self-contained', 'true', '--output', $singleFileOutput,
        '-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true', "-p:RestoreConfigFile=$nugetConfig") + $packageProperties)
    $executableName = if ($IsWindows) { 'DotCraft.Harness.Consumer.exe' } else { 'DotCraft.Harness.Consumer' }
    & (Join-Path $singleFileOutput $executableName) $pluginOutput
    if ($LASTEXITCODE -ne 0) { throw "Extracted single-file consumer failed with exit code $LASTEXITCODE." }
    $extractedCore = @(Get-ChildItem -LiteralPath $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR -Filter 'DotCraft.Core.dll' -File -Recurse)
    if ($extractedCore.Count -ne 1) { throw 'Expected one extracted single-file host reference pack.' }
    Assert-AuthoringReferencePack $extractedCore[0].DirectoryName
    $completed = $true
    Write-Host "Validated DotCraft.Harness $packageVersion from the exact local candidate, including extracted single-file authoring."
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundleRoot
    if ($completed -and !$KeepArtifacts) {
        $resolvedProbe = (Resolve-Path -LiteralPath $probeRoot).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ((Split-Path -Parent $resolvedProbe) -ne $tempRoot -or
            !(Split-Path -Leaf $resolvedProbe).StartsWith('dotcraft-harness-package-probe-')) {
            throw "Refusing cleanup outside the generated temporary probe: $resolvedProbe"
        }
        Remove-Item -LiteralPath $resolvedProbe -Recurse -Force
    }
    else {
        Write-Host "Package smoke artifacts retained at $probeRoot"
    }
}
