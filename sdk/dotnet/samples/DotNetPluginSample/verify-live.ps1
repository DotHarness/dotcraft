<#
.SYNOPSIS
    Runs the managed plugin sample through a real AppServer and local model provider.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DotCraftBin,
    [string]$ProviderId,
    [string]$Model,
    [string]$Report,
    [string]$WorkRoot,
    [ValidateRange(1, 60)]
    [int]$TimeoutMinutes = 5,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'
$sample = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $sample '..' '..' '..' '..')).Path

& (Join-Path $sample 'verify.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$arguments = @(
    'run', '--project', (Join-Path $repoRoot 'tests/DotCraft.AppServerTestClient/DotCraft.AppServerTestClient.csproj'),
    '--', '--dotcraft-bin', (Resolve-Path $DotCraftBin).Path,
    'dotnet-plugin-smoke', '--bundles', (Join-Path $sample 'bundles'),
    '--timeout-minutes', $TimeoutMinutes
)
if ($ProviderId) { $arguments += @('--provider-id', $ProviderId) }
if ($Model) { $arguments += @('--model', $Model) }
if ($Report) { $arguments += @('--report', $Report) }
if ($WorkRoot) { $arguments += @('--work-root', $WorkRoot) }
if ($KeepWorkspace) { $arguments += '--keep-workspace' }

dotnet @arguments
exit $LASTEXITCODE
