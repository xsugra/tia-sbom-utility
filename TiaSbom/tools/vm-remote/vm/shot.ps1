# Screenshot of the whole desktop, optional red frames: -Marks "x,y,w,h,label;x,y,w,h,label"
param([string]$Path = 'C:\TiaSbomTest\shots\shot.png', [string]$Marks = '')
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Dpi -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[W.Dpi]::SetProcessDPIAware() | Out-Null
$b = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
if ($Marks) {
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Red), 4
  $font = New-Object System.Drawing.Font 'Segoe UI', 16, ([System.Drawing.FontStyle]::Bold)
  foreach ($m in $Marks -split ';') {
    $p = $m -split ','
    $g.DrawRectangle($pen, [int]$p[0], [int]$p[1], [int]$p[2], [int]$p[3])
    if ($p.Count -gt 4) { $g.FillRectangle([System.Drawing.Brushes]::Red, [int]$p[0], [int]$p[1] - 30, 30 + 12 * $p[4].Length, 30); $g.DrawString($p[4], $font, [System.Drawing.Brushes]::White, [int]$p[0] + 4, [int]$p[1] - 30) }
  }
}
New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
$bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Path $($b.Width)x$($b.Height)"
