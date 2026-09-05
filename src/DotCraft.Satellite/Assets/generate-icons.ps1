# Builds the Satellite application icon and the four tray state icons from the shared DotCraft
# mark. Run it from this directory after the mark changes; the generated .ico files are committed
# so the build needs no image tooling.
#
#   pwsh -NoProfile -File generate-icons.ps1

[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\..\..\desktop\resources\icon.ico'),
    [string]$OutputDirectory = $PSScriptRoot
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64)
$states = [ordered]@{
    'offline'   = '#8A8F98'
    'standby'   = '#4566CC'
    'connected' = '#3FB950'
    'paused'    = '#D9A22B'
}

function New-IcoFile {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap[]]$Frames,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $payloads = foreach ($frame in $Frames) {
        $buffer = New-Object System.IO.MemoryStream
        $frame.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
        , $buffer.ToArray()
    }

    $stream = [System.IO.File]::Create($Path)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Frames.Count)
        $offset = 6 + (16 * $Frames.Count)
        for ($index = 0; $index -lt $Frames.Count; $index++) {
            $side = $Frames[$index].Width
            $writer.Write([byte]($(if ($side -ge 256) { 0 } else { $side })))
            $writer.Write([byte]($(if ($side -ge 256) { 0 } else { $side })))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $payloads[$index].Length
        }
        foreach ($payload in $payloads) {
            $writer.Write($payload)
        }
        $writer.Flush()
    }
    finally {
        $stream.Dispose()
    }
}

function New-Frame {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Image]$Mark,
        [Parameter(Mandatory = $true)][int]$Side,
        [string]$DotColor
    )

    $bitmap = New-Object System.Drawing.Bitmap($Side, $Side)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        if ([string]::IsNullOrEmpty($DotColor)) {
            $graphics.DrawImage($Mark, 0, 0, $Side, $Side)
        }
        else {
            # The mark shrinks so the state dot never covers it.
            $inset = [Math]::Max(1, [int]([Math]::Round($Side * 0.12)))
            $graphics.DrawImage($Mark, 0, 0, $Side - $inset, $Side - $inset)

            $dot = [Math]::Max(6, [int]([Math]::Round($Side * 0.44)))
            $ring = [Math]::Max(1.0, $Side * 0.09)
            $x = $Side - $dot - 1
            $y = $Side - $dot - 1
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 0, 0, 0), $ring)
            $brush = New-Object System.Drawing.SolidBrush(
                [System.Drawing.ColorTranslator]::FromHtml($DotColor))
            try {
                $graphics.DrawEllipse($pen, $x, $y, $dot, $dot)
                $graphics.FillEllipse($brush, $x, $y, $dot, $dot)
            }
            finally {
                $pen.Dispose()
                $brush.Dispose()
            }
        }
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$icon = New-Object System.Drawing.Icon($sourcePath, 256, 256)
$mark = $icon.ToBitmap()

try {
    $appFrames = foreach ($side in $sizes) { New-Frame -Mark $mark -Side $side }
    New-IcoFile -Frames $appFrames -Path (Join-Path $OutputDirectory 'satellite.ico')
    $appFrames | ForEach-Object { $_.Dispose() }
    Write-Host 'satellite.ico'

    foreach ($state in $states.GetEnumerator()) {
        $frames = foreach ($side in $sizes) {
            New-Frame -Mark $mark -Side $side -DotColor $state.Value
        }
        $path = Join-Path $OutputDirectory ("tray-" + $state.Key + ".ico")
        New-IcoFile -Frames $frames -Path $path
        $frames | ForEach-Object { $_.Dispose() }
        Write-Host ("tray-" + $state.Key + ".ico")
    }
}
finally {
    $mark.Dispose()
    $icon.Dispose()
}
