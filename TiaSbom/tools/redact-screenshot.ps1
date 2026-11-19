<#
  Prepares a screenshot for the documentation: crops it, covers private text and adds numbered red frames.

    .\redact-screenshot.ps1 -Source shot.png -Out docs\img\x.png -Crop "x,y,w,h" `
        -Masks "x,y,w,h,replacement text;..." -Marks "x,y,w,h,label;..."

  Coordinates refer to the source image. Masks are drawn first, marks on top. Customer names, project paths and
  machine names must never reach docs/img (CLAUDE.md), so every TIA screenshot passes through this script.
#>
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Out,
    [string]$Crop,
    [string]$Masks = '',
    [string]$Marks = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$image = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)
$canvas = New-Object System.Drawing.Bitmap $image.Width, $image.Height
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.DrawImage($image, 0, 0, $image.Width, $image.Height)
$image.Dispose()
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

function Parts([string]$item) { $p = $item -split ',', 5; return $p }

foreach ($mask in ($Masks -split ';' | Where-Object { $_ })) {
    $p = Parts $mask
    $rect = New-Object System.Drawing.Rectangle ([int]$p[0]), ([int]$p[1]), ([int]$p[2]), ([int]$p[3])
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 225, 225, 225))), $rect)
    $g.DrawRectangle([System.Drawing.Pens]::Gray, $rect)
    if ($p.Count -gt 4 -and $p[4]) {
        $font = New-Object System.Drawing.Font 'Segoe UI', 8, ([System.Drawing.FontStyle]::Italic)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
        $g.DrawString($p[4], $font, [System.Drawing.Brushes]::DimGray, [System.Drawing.RectangleF]$rect, $format)
    }
}

$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Red), 3
$labelFont = New-Object System.Drawing.Font 'Segoe UI', 11, ([System.Drawing.FontStyle]::Bold)
foreach ($mark in ($Marks -split ';' | Where-Object { $_ })) {
    $p = Parts $mark
    $g.DrawRectangle($pen, [int]$p[0], [int]$p[1], [int]$p[2], [int]$p[3])
    if ($p.Count -gt 4 -and $p[4]) {
        $size = $g.MeasureString($p[4], $labelFont)
        $lx = [int]$p[0] - 2; $ly = [Math]::Max(0, [int]$p[1] - [int]$size.Height - 2)
        $g.FillRectangle([System.Drawing.Brushes]::Red, $lx, $ly, [int]$size.Width + 4, [int]$size.Height + 2)
        $g.DrawString($p[4], $labelFont, [System.Drawing.Brushes]::White, $lx + 2, $ly + 1)
    }
}
$g.Dispose()

$result = $canvas
if ($Crop) {
    $c = $Crop -split ','
    $area = New-Object System.Drawing.Rectangle ([int]$c[0]), ([int]$c[1]), ([int]$c[2]), ([int]$c[3])
    $result = $canvas.Clone($area, $canvas.PixelFormat)
    $canvas.Dispose()
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$result.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$result.Dispose()
"saved $Out"
