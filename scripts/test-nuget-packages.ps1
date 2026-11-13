param(
    [string]$PackageSource,
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$catalog = & (Join-Path $PSScriptRoot 'nuget-packages.ps1')
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) "dotcraft-package-probe-$([guid]::NewGuid().ToString('N'))"
$probeRoot = [IO.Path]::GetFullPath($probeRoot)
New-Item -ItemType Directory -Path $probeRoot | Out-Null
$completed = $false
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$previousBundleRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR

# Closures excluded by the dependency budget in specs/sdk/nuget-packages.md.
$kernelExcluded = @('Microsoft.CodeAnalysis.*', 'Jint', 'Tomlyn', 'DotCraft.Protocol')
$providerExcluded = @('DotCraft.Core', 'DotCraft.Runtime', 'DotCraft.Harness', 'DotCraft.Protocol')

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

function New-Probe {
    param([string]$Name, [string[]]$Packages, [string]$Source)
    $destination = Join-Path $probeRoot $Name
    New-Item -ItemType Directory -Path $destination | Out-Null
    $references = ($Packages | ForEach-Object { '    <PackageReference Include="' + $_ + '" Version="[$(DotCraftPackageVersion)]" />' }) -join [Environment]::NewLine
    $project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
REFERENCES
  </ItemGroup>
</Project>
'@
    [IO.File]::WriteAllText((Join-Path $destination "$Name.csproj"), $project.Replace('REFERENCES', $references))
    [IO.File]::WriteAllText((Join-Path $destination 'Program.cs'), $Source)
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

function Read-Nuspec {
    param($Package)
    $entry = @($Package.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
    if ($entry.Count -ne 1) { throw 'Expected one nuspec per package.' }
    $reader = [IO.StreamReader]::new($entry[0].Open())
    try { return [xml]$reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Assert-PackageShape {
    param([string]$Source, [string]$Version)
    $generatorHashes = @{}
    foreach ($item in $catalog) {
        $path = Join-Path $Source "$($item.Id).$Version.nupkg"
        if (!(Test-Path -LiteralPath $path)) { throw "Missing package candidate: $($item.Id) $Version" }
        $package = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $nuspec = Read-Nuspec $package
            $dependencies = @($nuspec.package.metadata.dependencies.group | ForEach-Object { $_.dependency }) | Where-Object { $_ }
            foreach ($dependency in $dependencies) {
                if ($dependency.id -like 'DotCraft.*' -and $dependency.version -cne "[$Version]") {
                    throw "$($item.Id) must depend on $($dependency.id) with the exact range [$Version], found $($dependency.version)."
                }
            }
            $assemblies = @($package.Entries | Where-Object { $_.FullName -like 'lib/*/DotCraft.*.dll' } | ForEach-Object { $_.Name })
            $expected = @("$($item.Id).dll")
            if ($item.Id -eq 'DotCraft.CSharp') { $expected += 'DotCraft.Generators.dll' }
            $unexpected = @($assemblies | Where-Object { $_ -notin $expected })
            if ($unexpected.Count -gt 0) { throw "$($item.Id) embeds other DotCraft assemblies: $($unexpected -join ', ')" }
            foreach ($entry in @($package.Entries | Where-Object { $_.Name -eq 'DotCraft.Generators.dll' })) {
                $generatorHashes["$($item.Id):$($entry.FullName)"] = Get-EntryHash $entry
            }
        }
        finally { $package.Dispose() }
    }
    if (!$generatorHashes.ContainsKey('DotCraft.Core:analyzers/dotnet/cs/DotCraft.Generators.dll')) {
        throw 'DotCraft.Core must carry the tool generator as an analyzer.'
    }
    if (!$generatorHashes.ContainsKey('DotCraft.CSharp:lib/net10.0/DotCraft.Generators.dll')) {
        throw 'DotCraft.CSharp must carry the tool generator as a runtime asset.'
    }
    if (@($generatorHashes.Values | Sort-Object -Unique).Count -ne 1) {
        throw 'Every packaged DotCraft.Generators.dll must be the same binary.'
    }
}

function Assert-Closure {
    param([string]$Project, [string[]]$Excluded)
    $assets = Get-Content -Raw (Join-Path (Split-Path -Parent $Project) 'obj/project.assets.json') | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
    foreach ($pattern in $Excluded) {
        $hits = @($libraries | Where-Object { $_ -like $pattern })
        if ($hits.Count -gt 0) { throw "$(Split-Path -Leaf $Project) restores excluded dependencies: $($hits -join ', ')" }
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
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceRoot = Join-Path $probeRoot 'source'
    New-Item -ItemType Directory -Path $sourceRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        foreach ($item in $catalog) {
            Invoke-Dotnet -Arguments @('pack', (Join-Path $repoRoot $item.Project), '--configuration', $Configuration, '--output', $sourceRoot)
        }
    }
    else {
        Get-ChildItem -LiteralPath (Resolve-Path -LiteralPath $PackageSource).Path -Filter 'DotCraft.*.nupkg' |
            Copy-Item -Destination $sourceRoot
    }

    $harness = @(Get-ChildItem -LiteralPath $sourceRoot -Filter 'DotCraft.Harness.*.nupkg')
    if ($harness.Count -ne 1) { throw 'Expected exactly one DotCraft.Harness candidate.' }
    $harnessPackage = [IO.Compression.ZipFile]::OpenRead($harness[0].FullName)
    try { $packageVersion = (Read-Nuspec $harnessPackage).package.metadata.version }
    finally { $harnessPackage.Dispose() }
    Assert-PackageShape -Source $sourceRoot -Version $packageVersion

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
    <packageSource key="candidate"><package pattern="DotCraft.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@)
    $packageProperties = @('-p:UseDotCraftPackages=true', "-p:DotCraftPackageVersion=$packageVersion")

    $providerProbe = New-Probe 'ProviderProbe' @('DotCraft.Agents.OpenAI') 'Console.WriteLine(typeof(DotCraft.Agents.OpenAIClientProvider).Assembly.GetName().Name);'
    $coreProbe = New-Probe 'CoreProbe' @('DotCraft.Core', 'DotCraft.Agents.OpenAI') 'Console.WriteLine(typeof(DotCraft.Sessions.ISessionService).Assembly.GetName().Name);'
    $harnessProbe = New-Probe 'HarnessProbe' @('DotCraft.Harness') 'Console.WriteLine(typeof(DotCraft.Harness.DotCraftHarnessOptions).Assembly.GetName().Name);'
    $pluginProject = Copy-Fixture 'DotCraft.Harness.PluginFixture'
    $consumerProject = Copy-Fixture 'DotCraft.Harness.Consumer'
    foreach ($project in @($providerProbe, $coreProbe, $harnessProbe, $pluginProject, $consumerProject)) {
        Invoke-Dotnet (@('restore', $project, '--configfile', $nugetConfig, '--packages', $env:NUGET_PACKAGES) + $packageProperties)
    }
    foreach ($probe in @($providerProbe, $coreProbe, $harnessProbe)) {
        Invoke-Dotnet (@('build', $probe, '--configuration', $Configuration, '--no-restore') + $packageProperties)
    }
    Assert-Closure -Project $providerProbe -Excluded ($providerExcluded + $kernelExcluded)
    Assert-Closure -Project $coreProbe -Excluded $kernelExcluded
    Assert-Closure -Project $harnessProbe -Excluded $kernelExcluded

    $pluginOutput = Join-Path $probeRoot 'plugin-output'
    $consumerOutput = Join-Path $probeRoot 'consumer-output'
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
    Write-Host "Validated $($catalog.Count) DotCraft packages $($packageVersion): shapes, dependency budgets, Harness hosting, and single-file authoring."
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundleRoot
    if ($completed -and !$KeepArtifacts) {
        $resolvedProbe = (Resolve-Path -LiteralPath $probeRoot).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ((Split-Path -Parent $resolvedProbe) -ne $tempRoot -or
            !(Split-Path -Leaf $resolvedProbe).StartsWith('dotcraft-package-probe-')) {
            throw "Refusing cleanup outside the generated temporary probe: $resolvedProbe"
        }
        Remove-Item -LiteralPath $resolvedProbe -Recurse -Force
    }
    else {
        Write-Host "Package smoke artifacts retained at $probeRoot"
    }
}
