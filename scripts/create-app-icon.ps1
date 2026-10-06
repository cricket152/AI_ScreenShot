$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconProjectRoot = Split-Path $PSScriptRoot -Parent
$iconSource = Join-Path $iconProjectRoot 'docs\icon-candidates\ai-screenshot-fluent-v1.png'
$iconAssets = Join-Path $iconProjectRoot 'src\ScreenShot.App\Assets'
[System.IO.Directory]::CreateDirectory($iconAssets) | Out-Null
$iconImage = [System.Drawing.Image]::FromFile($iconSource)
$iconFrames = [System.Collections.Generic.List[byte[]]]::new()
$iconSizes = @(16,20,24,32,40,48,64,96,128,256)
try {
    foreach ($iconSize in $iconSizes) {
        $iconBitmap = [System.Drawing.Bitmap]::new($iconSize,$iconSize,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
        $iconMemory = [System.IO.MemoryStream]::new()
        try {
            $iconGraphics.Clear([System.Drawing.Color]::Transparent)
            $iconGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $iconGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $iconGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $iconGraphics.DrawImage($iconImage,0,0,$iconSize,$iconSize)
            $iconBitmap.Save($iconMemory,[System.Drawing.Imaging.ImageFormat]::Png)
            $iconFrames.Add($iconMemory.ToArray())
        } finally { $iconMemory.Dispose(); $iconGraphics.Dispose(); $iconBitmap.Dispose() }
    }
} finally { $iconImage.Dispose() }
$iconOutput = [System.IO.File]::Create((Join-Path $iconAssets 'AppIcon.ico'))
$iconWriter = [System.IO.BinaryWriter]::new($iconOutput)
try {
    $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$iconSizes.Count)
    $iconOffset = 6 + 16 * $iconSizes.Count
    for ($iconIndex = 0; $iconIndex -lt $iconSizes.Count; $iconIndex++) {
        $iconDimension = if ($iconSizes[$iconIndex] -eq 256) { 0 } else { $iconSizes[$iconIndex] }
        $iconWriter.Write([byte]$iconDimension); $iconWriter.Write([byte]$iconDimension)
        $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
        $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconFrames[$iconIndex].Length); $iconWriter.Write([uint32]$iconOffset)
        $iconOffset += $iconFrames[$iconIndex].Length
    }
    foreach ($iconFrame in $iconFrames) { $iconWriter.Write($iconFrame) }
} finally { $iconWriter.Dispose() }
[System.IO.File]::WriteAllBytes((Join-Path $iconAssets 'AppIcon.png'),$iconFrames[$iconFrames.Count-1])
Write-Output 'Created AppIcon.ico (10 sizes, 16-256px) and AppIcon.png from the approved artwork.'
