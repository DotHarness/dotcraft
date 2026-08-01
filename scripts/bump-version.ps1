[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Assert-Exists {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found: $Path"
    }
}

function Replace-Regex {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [switch]$Singleline,
        [switch]$Multiline
    )

    $options = [System.Text.RegularExpressions.RegexOptions]::None
    if ($Singleline) { $options = $options -bor [System.Text.RegularExpressions.RegexOptions]::Singleline }
    if ($Multiline) { $options = $options -bor [System.Text.RegularExpressions.RegexOptions]::Multiline }

    if (-not [System.Text.RegularExpressions.Regex]::IsMatch($Content, $Pattern, $options)) {
        throw "Pattern not found: $Pattern"
    }

    return [System.Text.RegularExpressions.Regex]::Replace($Content, $Pattern, $Replacement, $options)
}

function Update-XmlVersionFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern "<AssemblyVersion>[^<]+</AssemblyVersion>" -Replacement "<AssemblyVersion>$NewVersion</AssemblyVersion>"
    $content = Replace-Regex -Content $content -Pattern "<Version>[^<]+</Version>" -Replacement "<Version>$NewVersion</Version>"
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-DotNetPackageVersionFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern "<Version>[^<]+</Version>" -Replacement "<Version>$NewVersion</Version>"
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-TomlVersionLine {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern '(^\s*version\s*=\s*")[^"]+(")' -Replacement ('${1}' + $NewVersion + '${2}') -Multiline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-PythonModuleVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern '(^\s*__version__\s*=\s*")[^"]+(")' -Replacement ('${1}' + $NewVersion + '${2}') -Multiline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-TypeScriptModuleVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern '(^\s*export\s+const\s+version\s*=\s*")[^"]+(";)' -Replacement ('${1}' + $NewVersion + '${2}') -Multiline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-PackageJsonVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $content = Replace-Regex -Content $content -Pattern '("version"\s*:\s*")[^"]+(")' -Replacement ('${1}' + $NewVersion + '${2}')
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-NpmLockRootAndWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RootName,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)

    $rootPattern = '(^\s*\{\s*"name"\s*:\s*"' + [System.Text.RegularExpressions.Regex]::Escape($RootName) + '"\s*,\s*"version"\s*:\s*")[^"]+(")'
    $content = Replace-Regex -Content $content -Pattern $rootPattern -Replacement ('${1}' + $NewVersion + '${2}') -Singleline -Multiline

    $workspacePattern = '(""\s*:\s*\{[\s\S]*?"name"\s*:\s*"' + [System.Text.RegularExpressions.Regex]::Escape($RootName) + '"[\s\S]*?"version"\s*:\s*")[^"]+(")'
    $content = Replace-Regex -Content $content -Pattern $workspacePattern -Replacement ('${1}' + $NewVersion + '${2}') -Singleline

    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-NpmLockWorkspaceVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$WorkspacePath,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $pattern = '("' + [System.Text.RegularExpressions.Regex]::Escape($WorkspacePath) + '"\s*:\s*\{\s*"name"\s*:\s*"[^"]+"\s*,\s*"version"\s*:\s*")[^"]+(")'
    $content = Replace-Regex -Content $content -Pattern $pattern -Replacement ('${1}' + $NewVersion + '${2}') -Singleline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

# A lock file names every package it owns, so a version left behind anywhere in one
# means a target was missed. This is what let the workspace entries sit at a stale
# version through several releases while the workspace package.json files moved on.
function Assert-NpmLockVersionsSynced {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    $content = [System.IO.File]::ReadAllText($Path)
    $pattern = '"name"\s*:\s*"(@dotcraft/[^"]+|dotcraft-desktop)"\s*,\s*"version"\s*:\s*"([^"]+)"'
    $stale = New-Object System.Collections.Generic.List[string]

    foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($content, $pattern)) {
        if ($match.Groups[2].Value -ne $NewVersion) {
            $stale.Add("$($match.Groups[1].Value) is still $($match.Groups[2].Value)") | Out-Null
        }
    }

    if ($stale.Count -gt 0) {
        throw "$Path was not fully updated: $($stale -join '; ')"
    }
}

function Update-NpmLockLinkedSdkVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $pattern = '("\.\./sdk/typescript"\s*:\s*\{\s*"name"\s*:\s*"@dotcraft/sdk"\s*,\s*"version"\s*:\s*")[^"]+(")'
    $content = Replace-Regex -Content $content -Pattern $pattern -Replacement ('${1}' + $NewVersion + '${2}') -Singleline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required. Example: 0.1.2"
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version'. Expected format: X.Y.Z"
}

$repoRoot = Split-Path -Parent $PSScriptRoot

$targets = @(
    @{ Type = "xml"; Path = "src/DotCraft.App/DotCraft.App.csproj" },
    @{ Type = "dotnetPackage"; Path = "sdk/dotnet/src/DotCraft.Sdk/DotCraft.Sdk.csproj" },
    @{ Type = "toml"; Path = "sdk/python/pyproject.toml" },
    @{ Type = "pythonModule"; Path = "sdk/python/dotcraft/__init__.py" },
    @{ Type = "packageJson"; Path = "desktop/package.json" },
    @{ Type = "npmLock"; Path = "desktop/package-lock.json"; Name = "dotcraft-desktop"; UpdateLinkedSdk = $true },
    @{ Type = "packageJson"; Path = "sdk/typescript/package.json" },
    @{ Type = "npmLock"; Path = "sdk/typescript/package-lock.json"; Name = "@dotcraft/sdk" },
    @{ Type = "typescriptModule"; Path = "sdk/typescript/src/index.ts" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-feishu/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-weixin/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-telegram/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-qq/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-wecom/package.json" }
)

# The lock file carries one entry per workspace beside its root entry. That list is
# derived from the workspace package.json targets above rather than repeated, so a
# new channel package cannot be added to one place and forgotten in the other.
$sdkWorkspaces = @(
    $targets |
        Where-Object { $_.Type -eq "packageJson" -and $_.Path -like "sdk/typescript/packages/*/package.json" } |
        ForEach-Object { ($_.Path -replace "^sdk/typescript/", "") -replace "/package\.json$", "" }
)

foreach ($target in $targets) {
    if ($target.Type -eq "npmLock" -and $target.Path -eq "sdk/typescript/package-lock.json") {
        $target.Workspaces = $sdkWorkspaces
    }
}

$updatedFiles = New-Object System.Collections.Generic.List[string]

foreach ($target in $targets) {
    $relativePath = $target.Path
    $absolutePath = Join-Path $repoRoot $relativePath
    Write-Host "Updating $relativePath -> $Version"

    switch ($target.Type) {
        "xml" {
            Update-XmlVersionFile -Path $absolutePath -NewVersion $Version
        }
        "dotnetPackage" {
            Update-DotNetPackageVersionFile -Path $absolutePath -NewVersion $Version
        }
        "toml" {
            Update-TomlVersionLine -Path $absolutePath -NewVersion $Version
        }
        "pythonModule" {
            Update-PythonModuleVersion -Path $absolutePath -NewVersion $Version
        }
        "typescriptModule" {
            Update-TypeScriptModuleVersion -Path $absolutePath -NewVersion $Version
        }
        "packageJson" {
            Update-PackageJsonVersion -Path $absolutePath -NewVersion $Version
        }
        "npmLock" {
            Update-NpmLockRootAndWorkspace -Path $absolutePath -RootName $target.Name -NewVersion $Version
            if ($target.ContainsKey("Workspaces")) {
                foreach ($workspace in $target.Workspaces) {
                    Update-NpmLockWorkspaceVersion -Path $absolutePath -WorkspacePath $workspace -NewVersion $Version
                }
            }
            if ($target.ContainsKey("UpdateLinkedSdk") -and $target.UpdateLinkedSdk) {
                Update-NpmLockLinkedSdkVersion -Path $absolutePath -NewVersion $Version
            }
            Assert-NpmLockVersionsSynced -Path $absolutePath -NewVersion $Version
        }
        default {
            throw "Unknown target type: $($target.Type)"
        }
    }

    $updatedFiles.Add($relativePath) | Out-Null
}

Write-Host ""
Write-Host "Version bump completed: $Version"
Write-Host "Updated files:"
foreach ($path in $updatedFiles) {
    Write-Host " - $path"
}
