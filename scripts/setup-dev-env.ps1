[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Yes,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Usage {
    Write-Host "DotCraft developer environment setup"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  setup.bat          Check and prompt to install missing tools"
    Write-Host "  setup.bat /check   Check only"
    Write-Host "  setup.bat /yes     Install missing tools without confirmation"
}

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ""
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
}

function Write-ToolResult {
    param([Parameter(Mandatory = $true)]$Result)

    if ($Result.Installed) {
        Write-Host "[OK]      " -ForegroundColor Green -NoNewline
    } else {
        Write-Host "[MISSING] " -ForegroundColor Yellow -NoNewline
    }

    Write-Host "$($Result.Name): $($Result.Detail)"
}

function New-ToolResult {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Installed,
        [Parameter(Mandatory = $true)][string]$Detail,
        $InstallAction = $null
    )

    return [pscustomobject]@{
        Id = $Id
        Name = $Name
        Installed = $Installed
        Detail = $Detail
        InstallAction = $InstallAction
    }
}

function New-WingetAction {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$DisplayCommand,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    return [pscustomobject]@{
        Name = $Name
        DisplayCommand = $DisplayCommand
        Arguments = $Arguments
    }
}

function ConvertTo-VersionOrNull {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    if ($Text -match '(\d+)\.(\d+)(?:\.(\d+))?') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = 0
        if ($Matches[3]) {
            $patch = [int]$Matches[3]
        }

        return New-Object System.Version($major, $minor, $patch)
    }

    return $null
}

function Test-DotNetSdk {
    $action = New-WingetAction `
        -Name ".NET 10 SDK" `
        -DisplayCommand "winget install --exact --id Microsoft.DotNet.SDK.10 --source winget --silent --accept-package-agreements --accept-source-agreements" `
        -Arguments @("install", "--exact", "--id", "Microsoft.DotNet.SDK.10", "--source", "winget", "--silent", "--accept-package-agreements", "--accept-source-agreements")

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return New-ToolResult -Id "dotnet" -Name ".NET SDK" -Installed $false -Detail "dotnet command not found; requires .NET SDK 10.x." -InstallAction $action
    }

    $sdkLines = @(& $dotnet.Source --list-sdks 2>$null)
    $sdkVersions = @()
    foreach ($line in $sdkLines) {
        $version = ConvertTo-VersionOrNull -Text $line
        if ($version) {
            $sdkVersions += $version
        }
    }

    $dotnet10 = @($sdkVersions | Where-Object { $_.Major -eq 10 } | Sort-Object -Descending | Select-Object -First 1)
    if ($dotnet10.Count -gt 0) {
        return New-ToolResult -Id "dotnet" -Name ".NET SDK" -Installed $true -Detail "found SDK $($dotnet10[0])."
    }

    if ($sdkVersions.Count -gt 0) {
        $installed = ($sdkVersions | Sort-Object -Descending | ForEach-Object { $_.ToString() }) -join ", "
        return New-ToolResult -Id "dotnet" -Name ".NET SDK" -Installed $false -Detail "found SDK(s) $installed; requires 10.x." -InstallAction $action
    }

    return New-ToolResult -Id "dotnet" -Name ".NET SDK" -Installed $false -Detail "dotnet exists, but no SDKs were reported; requires SDK 10.x." -InstallAction $action
}

function Test-NodeToolchain {
    $action = New-WingetAction `
        -Name "Node.js LTS" `
        -DisplayCommand "winget install --exact --id OpenJS.NodeJS.LTS --source winget --silent --accept-package-agreements --accept-source-agreements" `
        -Arguments @("install", "--exact", "--id", "OpenJS.NodeJS.LTS", "--source", "winget", "--silent", "--accept-package-agreements", "--accept-source-agreements")

    $node = Get-Command node -ErrorAction SilentlyContinue
    $npm = Get-Command npm -ErrorAction SilentlyContinue

    if (-not $node -and -not $npm) {
        return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $false -Detail "node and npm commands not found; requires Node.js >=20 and npm >=7." -InstallAction $action
    }

    if (-not $node) {
        return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $false -Detail "node command not found; requires Node.js >=20." -InstallAction $action
    }

    if (-not $npm) {
        return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $false -Detail "npm command not found; requires npm >=7." -InstallAction $action
    }

    $nodeText = (& $node.Source --version 2>$null | Select-Object -First 1)
    $npmText = (& $npm.Source --version 2>$null | Select-Object -First 1)
    $nodeVersion = ConvertTo-VersionOrNull -Text $nodeText
    $npmVersion = ConvertTo-VersionOrNull -Text $npmText

    if (-not $nodeVersion -or $nodeVersion.Major -lt 20) {
        return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $false -Detail "found Node $nodeText; requires Node.js >=20." -InstallAction $action
    }

    if (-not $npmVersion -or $npmVersion.Major -lt 7) {
        return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $false -Detail "found npm $npmText; requires npm >=7." -InstallAction $action
    }

    return New-ToolResult -Id "node" -Name "Node.js/npm" -Installed $true -Detail "found Node $nodeText, npm $npmText."
}

function Test-RustToolchain {
    $action = New-WingetAction `
        -Name "Rustup/Rust stable toolchain" `
        -DisplayCommand "winget install --exact --id Rustlang.Rustup --source winget --silent --accept-package-agreements --accept-source-agreements" `
        -Arguments @("install", "--exact", "--id", "Rustlang.Rustup", "--source", "winget", "--silent", "--accept-package-agreements", "--accept-source-agreements")

    $cargo = Get-Command cargo -ErrorAction SilentlyContinue
    $rustc = Get-Command rustc -ErrorAction SilentlyContinue

    if (-not $cargo -and -not $rustc) {
        return New-ToolResult -Id "rust" -Name "Rust toolchain" -Installed $false -Detail "cargo and rustc commands not found." -InstallAction $action
    }

    if (-not $cargo) {
        return New-ToolResult -Id "rust" -Name "Rust toolchain" -Installed $false -Detail "cargo command not found." -InstallAction $action
    }

    if (-not $rustc) {
        return New-ToolResult -Id "rust" -Name "Rust toolchain" -Installed $false -Detail "rustc command not found." -InstallAction $action
    }

    $cargoText = (& $cargo.Source --version 2>$null | Select-Object -First 1)
    $rustcText = (& $rustc.Source --version 2>$null | Select-Object -First 1)
    return New-ToolResult -Id "rust" -Name "Rust toolchain" -Installed $true -Detail "found $rustcText, $cargoText."
}

function Get-VsWherePath {
    $candidates = @()
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $candidates += Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe"
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command vswhere -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Find-VcVars64 {
    $programFilesX86 = ${env:ProgramFiles(x86)}
    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $roots += Join-Path $env:ProgramFiles "Microsoft Visual Studio\2022"
    }

    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $roots += Join-Path $programFilesX86 "Microsoft Visual Studio\2022"
    }

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        $editions = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)
        foreach ($edition in $editions) {
            $vcVarsPath = Join-Path $edition.FullName "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path -LiteralPath $vcVarsPath) {
                return $vcVarsPath
            }
        }
    }

    return $null
}

function Test-MsvcToolchain {
    $action = New-WingetAction `
        -Name "Visual Studio 2022 Build Tools with C++ workload" `
        -DisplayCommand 'winget install --exact --id Microsoft.VisualStudio.2022.BuildTools --source winget --accept-package-agreements --accept-source-agreements --override "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"' `
        -Arguments @("install", "--exact", "--id", "Microsoft.VisualStudio.2022.BuildTools", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--override", "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended")

    $vswhere = Get-VsWherePath
    if ($vswhere) {
        $args = @("-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath")
        $installPath = @(& $vswhere @args 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        if ($installPath.Count -gt 0) {
            $vcVarsPath = Join-Path $installPath[0] "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path -LiteralPath $vcVarsPath) {
                return New-ToolResult -Id "msvc" -Name "MSVC C++ Build Tools" -Installed $true -Detail "found VC tools at $($installPath[0])."
            }

            return New-ToolResult -Id "msvc" -Name "MSVC C++ Build Tools" -Installed $true -Detail "found Visual Studio VC tools component at $($installPath[0])."
        }
    }

    $vcVars = Find-VcVars64
    if ($vcVars) {
        return New-ToolResult -Id "msvc" -Name "MSVC C++ Build Tools" -Installed $true -Detail "found $vcVars."
    }

    return New-ToolResult -Id "msvc" -Name "MSVC C++ Build Tools" -Installed $false -Detail "Visual Studio C++ tools were not found; Rust Windows native builds need the MSVC linker and Windows SDK." -InstallAction $action
}

function Test-Winget {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        return $winget.Source
    }

    return $null
}

function Update-ProcessPath {
    $paths = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    $scopes = @("Machine", "User", "Process")

    foreach ($scope in $scopes) {
        $pathValue = [Environment]::GetEnvironmentVariable("Path", $scope)
        if ([string]::IsNullOrWhiteSpace($pathValue)) {
            continue
        }

        foreach ($entry in $pathValue.Split(";")) {
            if ([string]::IsNullOrWhiteSpace($entry)) {
                continue
            }

            $trimmed = $entry.Trim()
            $key = $trimmed.ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $paths.Add($trimmed) | Out-Null
            }
        }
    }

    $knownToolPaths = @(
        (Join-Path $env:USERPROFILE ".cargo\bin"),
        (Join-Path $env:ProgramFiles "dotnet"),
        (Join-Path $env:ProgramFiles "nodejs")
    )

    foreach ($entry in $knownToolPaths) {
        if ((Test-Path -LiteralPath $entry) -and -not $seen.ContainsKey($entry.ToLowerInvariant())) {
            $seen[$entry.ToLowerInvariant()] = $true
            $paths.Add($entry) | Out-Null
        }
    }

    $env:Path = $paths -join ";"
}

function Get-ToolResults {
    return @(
        (Test-DotNetSdk),
        (Test-NodeToolchain),
        (Test-RustToolchain),
        (Test-MsvcToolchain)
    )
}

function Write-ManualInstallHelp {
    Write-Host ""
    Write-Host "WinGet was not found, so setup cannot install missing tools automatically." -ForegroundColor Yellow
    Write-Host "Install the missing tools manually from official sources, then rerun setup.bat /check:"
    Write-Host "  .NET 10 SDK: https://learn.microsoft.com/dotnet/core/install/windows"
    Write-Host "  Node.js LTS: https://nodejs.org/en/download"
    Write-Host "  Rustup:      https://rust-lang.org/tools/install/"
    Write-Host "  MSVC tools:  https://rust-lang.github.io/rustup/installation/windows-msvc.html"
}

function Invoke-InstallAction {
    param(
        [Parameter(Mandatory = $true)][string]$WingetPath,
        [Parameter(Mandatory = $true)]$Action
    )

    Write-Host ""
    Write-Host "Installing $($Action.Name)..." -ForegroundColor Cyan
    Write-Host "> $($Action.DisplayCommand)" -ForegroundColor Gray
    & $WingetPath @($Action.Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "WinGet install failed for $($Action.Name) with exit code $LASTEXITCODE."
    }
}

function Main {
    if ($Help) {
        Show-Usage
        return 0
    }

    Write-Section -Text "DotCraft Developer Environment Setup"
    if ($Check) {
        Write-Host "Mode: check only"
    } elseif ($Yes) {
        Write-Host "Mode: install missing tools without confirmation"
    } else {
        Write-Host "Mode: check and prompt before installing"
    }

    Write-Section -Text "Checking required tools"
    $results = Get-ToolResults
    foreach ($result in $results) {
        Write-ToolResult -Result $result
    }

    $missing = @($results | Where-Object { -not $_.Installed })
    if ($missing.Count -eq 0) {
        Write-Host ""
        Write-Host "All required tools are available. You can run build.bat now." -ForegroundColor Green
        return 0
    }

    Write-Host ""
    Write-Host "Missing tools:" -ForegroundColor Yellow
    foreach ($result in $missing) {
        Write-Host "  - $($result.Name): $($result.Detail)"
    }

    $actions = @($missing | ForEach-Object { $_.InstallAction } | Where-Object { $_ -ne $null })

    if ($Check) {
        Write-Host ""
        Write-Host "Check mode does not install anything. Run setup.bat to install missing tools." -ForegroundColor Yellow
        if ($actions.Count -gt 0) {
            Write-Host ""
            Write-Host "Planned WinGet commands:"
            foreach ($action in $actions) {
                Write-Host "  $($action.DisplayCommand)"
            }
        }

        return 1
    }

    $wingetPath = Test-Winget
    if (-not $wingetPath) {
        Write-ManualInstallHelp
        return 1
    }

    Write-Host ""
    Write-Host "The following WinGet commands will be run:" -ForegroundColor Cyan
    foreach ($action in $actions) {
        Write-Host "  $($action.DisplayCommand)"
    }

    if (-not $Yes) {
        Write-Host ""
        $answer = Read-Host "Install missing tools now? [y/N]"
        if ($answer -notmatch '^(y|yes)$') {
            Write-Host "Setup cancelled. No tools were installed." -ForegroundColor Yellow
            return 1
        }
    }

    foreach ($action in $actions) {
        Invoke-InstallAction -WingetPath $wingetPath -Action $action
    }

    Write-Section -Text "Refreshing PATH and rechecking"
    Update-ProcessPath
    $afterResults = Get-ToolResults
    foreach ($result in $afterResults) {
        Write-ToolResult -Result $result
    }

    $stillMissing = @($afterResults | Where-Object { -not $_.Installed })
    if ($stillMissing.Count -gt 0) {
        Write-Host ""
        Write-Host "Some tools are still not visible in this terminal." -ForegroundColor Yellow
        Write-Host "Restart the terminal, then run setup.bat /check before build.bat."
        return 1
    }

    Write-Host ""
    Write-Host "Setup complete. You can run build.bat now." -ForegroundColor Green
    Write-Host "If build.bat cannot find a newly installed command, restart the terminal and rerun setup.bat /check."
    return 0
}

try {
    $exitCode = Main
    exit $exitCode
} catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
