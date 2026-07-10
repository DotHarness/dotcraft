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
        (Test-NodeToolchain)
    )
}

function Get-RepositoryRoot {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        return $null
    }

    $root = @(& $git.Source rev-parse --show-toplevel 2>$null | Select-Object -First 1)
    if ($root.Count -eq 0 -or [string]::IsNullOrWhiteSpace($root[0])) {
        return $null
    }

    return $root[0]
}

function Test-HooksPathValue {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedValue = $Value.Trim().TrimEnd($pathSeparators)
    if ($normalizedValue -eq ".githooks") {
        return $true
    }

    $expected = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot ".githooks")).TrimEnd($pathSeparators)
    if ([System.IO.Path]::IsPathRooted($normalizedValue)) {
        $actual = [System.IO.Path]::GetFullPath($normalizedValue).TrimEnd($pathSeparators)
    } else {
        $actual = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $normalizedValue)).TrimEnd($pathSeparators)
    }

    return [string]::Equals($actual, $expected, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-GitHooks {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $false -Detail "git command not found; cannot configure repository hooks."
    }

    $repositoryRoot = Get-RepositoryRoot
    if (-not $repositoryRoot) {
        return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $false -Detail "current directory is not inside a Git repository."
    }

    $hookPath = Join-Path $repositoryRoot ".githooks\pre-commit"
    if (-not (Test-Path -LiteralPath $hookPath)) {
        return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $false -Detail "missing .githooks/pre-commit."
    }

    $configured = @(& $git.Source -C $repositoryRoot config --get core.hooksPath 2>$null | Select-Object -First 1)
    if ($configured.Count -eq 0 -or [string]::IsNullOrWhiteSpace($configured[0])) {
        return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $false -Detail "core.hooksPath is not configured; expected .githooks."
    }

    if (Test-HooksPathValue -RepositoryRoot $repositoryRoot -Value $configured[0]) {
        return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $true -Detail "core.hooksPath is configured as $($configured[0])."
    }

    return New-ToolResult -Id "githooks" -Name "Git hooks" -Installed $false -Detail "core.hooksPath is '$($configured[0])'; expected .githooks."
}

function Enable-GitHooks {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "git command not found; cannot configure repository hooks."
    }

    $repositoryRoot = Get-RepositoryRoot
    if (-not $repositoryRoot) {
        throw "Current directory is not inside a Git repository."
    }

    & $git.Source -C $repositoryRoot config core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set core.hooksPath to .githooks."
    }
}

function Write-ManualInstallHelp {
    Write-Host ""
    Write-Host "WinGet was not found, so setup cannot install missing tools automatically." -ForegroundColor Yellow
    Write-Host "Install the missing tools manually from official sources, then rerun setup.bat /check:"
    Write-Host "  .NET 10 SDK: https://learn.microsoft.com/dotnet/core/install/windows"
    Write-Host "  Node.js LTS: https://nodejs.org/en/download"
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

    Write-Section -Text "Checking Git hooks"
    $hooksResult = Test-GitHooks
    Write-ToolResult -Result $hooksResult

    $missing = @($results | Where-Object { -not $_.Installed })
    $hooksMissing = -not $hooksResult.Installed
    if ($missing.Count -eq 0 -and -not $hooksMissing) {
        Write-Host ""
        Write-Host "All required tools are available. You can run build.bat now." -ForegroundColor Green
        return 0
    }

    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "Missing tools:" -ForegroundColor Yellow
        foreach ($result in $missing) {
            Write-Host "  - $($result.Name): $($result.Detail)"
        }
    }

    if ($hooksMissing) {
        Write-Host ""
        Write-Host "Git hooks are not ready:" -ForegroundColor Yellow
        Write-Host "  - $($hooksResult.Detail)"
    }

    $actions = @($missing | ForEach-Object { $_.InstallAction } | Where-Object { $_ -ne $null })

    if ($Check) {
        Write-Host ""
        Write-Host "Check mode does not install tools or change Git config. Run setup.bat to fix missing setup steps." -ForegroundColor Yellow
        if ($actions.Count -gt 0) {
            Write-Host ""
            Write-Host "Planned WinGet commands:"
            foreach ($action in $actions) {
                Write-Host "  $($action.DisplayCommand)"
            }
        }

        return 1
    }

    if ($missing.Count -gt 0) {
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
    }

    if ($hooksMissing) {
        Write-Section -Text "Configuring Git hooks"
        Enable-GitHooks
        $afterHooksResult = Test-GitHooks
        Write-ToolResult -Result $afterHooksResult
        if (-not $afterHooksResult.Installed) {
            Write-Host ""
            Write-Host "Git hooks are still not configured correctly." -ForegroundColor Yellow
            return 1
        }
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
