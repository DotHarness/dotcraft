[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sample = $PSScriptRoot
$source = Join-Path $sample 'Desktop'
$sdkRoot = [IO.Path]::GetFullPath((Join-Path $sample '../../../typescript'))
$desktopSdkRoot = Join-Path $sdkRoot 'packages/plugin'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$packagesRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ("dotcraft-desktop-sample-" + [Guid]::NewGuid().ToString('N'))))
$modules = @(
    @{ Source = 'Core'; Bundle = 'acme.review-core' },
    @{ Source = 'Consumer'; Bundle = 'acme.review-consumer' }
)
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

foreach ($module in $modules) {
    $sourceDist = [IO.Path]::GetFullPath((Join-Path $source ($module.Source + '/dist')))
    $desktopRoot = [IO.Path]::GetFullPath((Join-Path $sample ('bundles/' + $module.Bundle + '/desktop')))
    $target = [IO.Path]::GetFullPath((Join-Path $desktopRoot 'dist'))
    if (-not $target.StartsWith($desktopRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Desktop Plugin output for $($module.Bundle) is outside the sample bundle."
    }
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $sourceDist -Destination $target -Recurse

    if (-not (Test-Path -LiteralPath (Join-Path $target 'index.mjs'))) {
        throw "Desktop Plugin $($module.Bundle) build did not produce index.mjs."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'index.css'))) {
        throw "Desktop Plugin $($module.Bundle) build did not produce index.css."
    }
    if ($module.Source -eq 'Core' -and -not (Get-ChildItem -LiteralPath (Join-Path $target 'assets') -Filter 'review-workspace-*.svg' -File -ErrorAction SilentlyContinue)) {
        throw 'Desktop Plugin acme.review-core build did not produce its background asset.'
    }
}
