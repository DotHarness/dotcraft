[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sample = $PSScriptRoot
$source = Join-Path $sample 'Desktop'
$target = Join-Path $sample 'bundles/acme.review-consumer/desktop/dist'
$sdkRoot = [IO.Path]::GetFullPath((Join-Path $sample '../../../typescript'))
$desktopSdkRoot = Join-Path $sdkRoot 'packages/plugin'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$packagesRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ("dotcraft-desktop-sample-" + [Guid]::NewGuid().ToString('N'))))
$desktopRoot = [IO.Path]::GetFullPath((Join-Path $sample 'bundles/acme.review-consumer/desktop'))
$target = [IO.Path]::GetFullPath($target)
if (-not $target.StartsWith($desktopRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Desktop Plugin output is outside the sample bundle.'
}
if (-not $packagesRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary package directory is outside the system temporary directory.'
}

New-Item -ItemType Directory -Path $packagesRoot | Out-Null
try {
    Push-Location $sdkRoot
    try {
        npm install --fund=false --audit=false
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript SDK install failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript SDK build failed.' }
        npm pack --pack-destination $packagesRoot
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript SDK pack failed.' }
    }
    finally {
        Pop-Location
    }

    Push-Location $desktopSdkRoot
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Desktop Plugin SDK build failed.' }
        npm pack --pack-destination $packagesRoot
        if ($LASTEXITCODE -ne 0) { throw 'Desktop Plugin SDK pack failed.' }
    }
    finally {
        Pop-Location
    }

    $sdkPackage = (Get-ChildItem -LiteralPath $packagesRoot -Filter 'dotcraft-sdk-*.tgz').FullName
    $desktopPackage = (Get-ChildItem -LiteralPath $packagesRoot -Filter 'dotcraft-plugin-*.tgz').FullName

    Push-Location $source
    try {
        npm install --no-save --package-lock=false --fund=false --audit=false $sdkPackage $desktopPackage
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Desktop Plugin build failed.' }
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item -LiteralPath $packagesRoot -Recurse -Force
}

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $source 'dist') -Destination $target -Recurse

if (-not (Test-Path -LiteralPath (Join-Path $target 'index.mjs'))) {
    throw 'Desktop Plugin build did not produce index.mjs.'
}
if (-not (Test-Path -LiteralPath (Join-Path $target 'index.css'))) {
    throw 'Desktop Plugin build did not produce index.css.'
}
