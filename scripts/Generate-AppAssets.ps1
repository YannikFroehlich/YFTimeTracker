[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\YFTimeTracker.App\Assets\YFTimeTrackerLogo.png')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$assetDirectory = Split-Path -Parent $resolvedSource

function New-LogoBitmap {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [int]$Width,

        [Parameter(Mandatory)]
        [int]$Height,

        [switch]$Letterbox
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 4, 9, 18))
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $sourceSide = [Math]::Min($Source.Width, $Source.Height)
        $sourceX = [int](($Source.Width - $sourceSide) / 2)
        $sourceY = [int](($Source.Height - $sourceSide) / 2)
        $destinationSide = if ($Letterbox) {
            [int]([Math]::Min($Width, $Height) * 0.88)
        }
        else {
            [Math]::Min($Width, $Height)
        }

        $destinationX = [int](($Width - $destinationSide) / 2)
        $destinationY = [int](($Height - $destinationSide) / 2)
        $destination = [System.Drawing.Rectangle]::new(
            $destinationX,
            $destinationY,
            $destinationSide,
            $destinationSide)
        $sourceRectangle = [System.Drawing.Rectangle]::new(
            $sourceX,
            $sourceY,
            $sourceSide,
            $sourceSide)

        $graphics.DrawImage(
            $Source,
            $destination,
            $sourceRectangle,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Save-PngAsset {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [int]$Width,

        [Parameter(Mandatory)]
        [int]$Height,

        [switch]$Letterbox
    )

    $bitmap = New-LogoBitmap -Source $Source -Width $Width -Height $Height -Letterbox:$Letterbox
    try {
        $targetPath = Join-Path $assetDirectory $Name
        $bitmap.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Generated $Name ($Width x $Height)"
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-MultiSizeIcon {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [string]$TargetPath
    )

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $entries = [System.Collections.Generic.List[object]]::new()

    foreach ($size in $sizes) {
        $bitmap = New-LogoBitmap -Source $Source -Width $size -Height $size
        $memory = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
            $entries.Add([pscustomobject]@{
                Size = $size
                Bytes = $memory.ToArray()
            })
        }
        finally {
            $memory.Dispose()
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.File]::Create($TargetPath)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$entries.Count)

        $offset = 6 + (16 * $entries.Count)
        foreach ($entry in $entries) {
            $dimension = if ($entry.Size -ge 256) { [byte]0 } else { [byte]$entry.Size }
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }

        foreach ($entry in $entries) {
            $writer.Write([byte[]]$entry.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    Write-Host "Generated $(Split-Path -Leaf $TargetPath) (multi-size icon)"
}

$sourceImage = [System.Drawing.Image]::FromFile($resolvedSource)
try {
    Save-PngAsset -Source $sourceImage -Name 'Square44x44Logo.png' -Width 44 -Height 44
    Save-PngAsset -Source $sourceImage -Name 'Square150x150Logo.png' -Width 150 -Height 150
    Save-PngAsset -Source $sourceImage -Name 'StoreLogo.png' -Width 50 -Height 50
    Save-PngAsset -Source $sourceImage -Name 'Wide310x150Logo.png' -Width 310 -Height 150 -Letterbox
    Save-PngAsset -Source $sourceImage -Name 'SplashScreen.png' -Width 620 -Height 300 -Letterbox
    Save-MultiSizeIcon -Source $sourceImage -TargetPath (Join-Path $assetDirectory 'YFTimeTracker.ico')
}
finally {
    $sourceImage.Dispose()
}
