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

function Update-NpmLockLinkedSdkVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $pattern = '("\.\./\.\."\s*:\s*\{\s*"name"\s*:\s*"@dotcraft/sdk"\s*,\s*"version"\s*:\s*")[^"]+(")'
    $content = Replace-Regex -Content $content -Pattern $pattern -Replacement ('${1}' + $NewVersion + '${2}') -Singleline
    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Update-CargoLockDotcraftTui {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    Assert-Exists -Path $Path
    $content = [System.IO.File]::ReadAllText($Path)
    $pattern = '(\[\[package\]\]\s*name\s*=\s*"dotcraft-tui"\s*version\s*=\s*")[^"]+(")'
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
    @{ Type = "toml"; Path = "sdk/python/pyproject.toml" },
    @{ Type = "toml"; Path = "tui/Cargo.toml" },
    @{ Type = "cargoLock"; Path = "tui/Cargo.lock" },
    @{ Type = "packageJson"; Path = "desktop/package.json" },
    @{ Type = "npmLock"; Path = "desktop/package-lock.json"; Name = "dotcraft-desktop" },
    @{ Type = "packageJson"; Path = "sdk/typescript/package.json" },
    @{ Type = "npmLock"; Path = "sdk/typescript/package-lock.json"; Name = "@dotcraft/sdk" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-feishu/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-weixin/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-telegram/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-qq/package.json" },
    @{ Type = "packageJson"; Path = "sdk/typescript/packages/channel-wecom/package.json" }
)

$updatedFiles = New-Object System.Collections.Generic.List[string]

foreach ($target in $targets) {
    $relativePath = $target.Path
    $absolutePath = Join-Path $repoRoot $relativePath
    Write-Host "Updating $relativePath -> $Version"

    switch ($target.Type) {
        "xml" {
            Update-XmlVersionFile -Path $absolutePath -NewVersion $Version
        }
        "toml" {
            Update-TomlVersionLine -Path $absolutePath -NewVersion $Version
        }
        "packageJson" {
            Update-PackageJsonVersion -Path $absolutePath -NewVersion $Version
        }
        "npmLock" {
            Update-NpmLockRootAndWorkspace -Path $absolutePath -RootName $target.Name -NewVersion $Version
            if ($target.ContainsKey("UpdateLinkedSdk") -and $target.UpdateLinkedSdk) {
                Update-NpmLockLinkedSdkVersion -Path $absolutePath -NewVersion $Version
            }
        }
        "cargoLock" {
            Update-CargoLockDotcraftTui -Path $absolutePath -NewVersion $Version
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
