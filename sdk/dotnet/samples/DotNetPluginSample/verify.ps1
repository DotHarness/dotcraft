<#
.SYNOPSIS
    Builds the sample bundles and checks them with the Host's own preflight and runtime.

.DESCRIPTION
    Three steps, in the order a plugin author works in: build both bundles into
    bundles/<pluginId>/lib, then run DotNetPluginSampleBundleTests over what was built. That suite
    admits each bundle through the real manifest parser - which runs the non-executing metadata
    preflight - trusts it by fingerprint, activates it with a real runtime manager, and asserts the
    observable consequence of every contribution point the sample covers: sections in the assembled
    prompt, Tools dispatched through the ordinary dispatcher, replacements displacing their named
    built-ins and the built-ins returning on teardown. A deterministic fake model also drives a real
    turn through the official Generic Host and ISessionService without network or user data access.

    The bundle facts are skipped unless DOTCRAFT_SAMPLE_BUNDLES points at built bundles, which is why
    the sample is verified from here rather than on every `dotnet test`. The catalog census in the
    same class is an ordinary fact, so a contribution point added to the kernel without a
    disposition in the sample's coverage table fails a plain `dotnet test`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sample = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $sample '..' '..' '..' '..')).Path

foreach ($project in 'ReviewProvider/ReviewProvider.csproj', 'ReviewConsumer/ReviewConsumer.csproj') {
    Write-Host "Building $project" -ForegroundColor Cyan
    dotnet build (Join-Path $sample $project) --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Preflighting, trusting, and activating the built bundles' -ForegroundColor Cyan
$env:DOTCRAFT_SAMPLE_BUNDLES = Join-Path $sample 'bundles'
try {
    dotnet test (Join-Path $repoRoot 'tests/DotCraft.Runtime.Tests/DotCraft.Runtime.Tests.csproj') `
        --filter 'FullyQualifiedName~DotNetPluginSampleBundleTests' --nologo
}
finally {
    Remove-Item Env:DOTCRAFT_SAMPLE_BUNDLES -ErrorAction SilentlyContinue
}
exit $LASTEXITCODE
