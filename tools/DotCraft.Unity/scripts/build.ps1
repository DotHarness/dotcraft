param([string]$BundledPluginRoot)
$ErrorActionPreference = 'Stop'
$toolRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$plugin = Join-Path $toolRoot 'plugin'
$nativeOutput = Join-Path $plugin 'native'
$nativeIntermediate = Join-Path $toolRoot 'native/obj'
New-Item -ItemType Directory -Force $nativeOutput,$nativeIntermediate | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ build tools are required.' }

$vcvars = Join-Path $visualStudio 'VC/Auxiliary/Build/vcvars64.bat'
$source = Join-Path $toolRoot 'native/Bootstrap.cpp'
$object = Join-Path $nativeIntermediate 'Bootstrap.obj'
$library = Join-Path $nativeIntermediate 'DotCraft.Unity.Native.lib'
$native = Join-Path $nativeOutput 'DotCraft.Unity.Native.dll'
$command = 'call "{0}" >nul && cl /nologo /LD /EHsc /MT /O2 "{1}" /Fo"{2}" /link /OUT:"{3}" /IMPLIB:"{4}"' -f $vcvars,$source,$object,$native,$library
& cmd.exe /d /c $command
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }

dotnet build (Join-Path $toolRoot 'src/DotCraft.Unity.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$managedOutput = Join-Path $plugin 'lib'
New-Item -ItemType Directory -Force $managedOutput | Out-Null
foreach ($name in @('DotCraft.Unity.dll','DotCraft.Unity.deps.json','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
    Copy-Item -LiteralPath (Join-Path $toolRoot "src/bin/Release/net10.0/$name") -Destination (Join-Path $managedOutput $name) -Force
}

if ($BundledPluginRoot) {
    $bundledRoot = [IO.Path]::GetFullPath($BundledPluginRoot)
    $bundledPlugin = Join-Path $bundledRoot 'unity'
    if (Test-Path -LiteralPath $bundledPlugin) {
        Remove-Item -LiteralPath $bundledPlugin -Recurse -Force
    }
    New-Item -ItemType Directory -Force $bundledPlugin | Out-Null
    Copy-Item -LiteralPath (Join-Path $plugin 'README.md') -Destination $bundledPlugin
    foreach ($directory in @('.craft-plugin','assets','skills','lib','native')) {
        Copy-Item -LiteralPath (Join-Path $plugin $directory) -Destination (Join-Path $bundledPlugin $directory) -Recurse
    }
    Write-Output $bundledPlugin
}

Write-Output $plugin
