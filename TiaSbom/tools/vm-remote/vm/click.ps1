# Mouse and keyboard in the interactive desktop.
#   -Do "click:x,y;dbl:x,y;right:x,y;keys:^s;text:abc;wait:2;item:Menu text;shot:name"
# item: clicks the visible menu item whose name is exactly the text (UI Automation), fails when there is none.
param([string]$Do)
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Name U -Namespace W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
'@
[W.U]::SetProcessDPIAware() | Out-Null
function Click([int]$x, [int]$y, [int]$down, [int]$up) { [W.U]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 150; [W.U]::mouse_event(1, 1, 0, 0, 0); [W.U]::mouse_event(1, -1, 0, 0, 0); Start-Sleep -Milliseconds 100; [W.U]::mouse_event($down, 0, 0, 0, 0); Start-Sleep -Milliseconds 60; [W.U]::mouse_event($up, 0, 0, 0, 0) }
function Find-Item([string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($e in $all) {
        $type = $e.Current.ControlType.ProgrammaticName
        if ($type -eq 'ControlType.MenuItem' -and -not $e.Current.IsOffscreen) { return $e }
    }
    return $null
}
foreach ($step in $Do -split ';') {
    $kind, $value = $step -split ':', 2
    switch ($kind) {
        'click' { $x, $y = $value -split ','; Click $x $y 2 4 }
        'dbl'   { $x, $y = $value -split ','; Click $x $y 2 4; Start-Sleep -Milliseconds 80; Click $x $y 2 4 }
        'right' { $x, $y = $value -split ','; Click $x $y 8 16 }
        'move'  { $x, $y = $value -split ','; [W.U]::SetCursorPos($x, $y) | Out-Null; [W.U]::mouse_event(1, 1, 0, 0, 0); [W.U]::mouse_event(1, -1, 0, 0, 0) }
        'keys'  { [System.Windows.Forms.SendKeys]::SendWait($value) }
        'text'  { [System.Windows.Forms.SendKeys]::SendWait(($value -replace '([+^%~(){}\[\]])', '{$1}')) }
        'wait'  { Start-Sleep -Seconds ([double]$value) }
        'raise' { & 'C:/TiaSbomTest/run/front.ps1' -Title $value | Out-Null }
        'shot'  { & 'C:/TiaSbomTest/run/shot.ps1' -Path "C:/TiaSbomTest/shots/$value.png" }
        'ocr'   {
            . 'C:/TiaSbomTest/run/ocr.ps1'
            function Norm([string]$t) { (($t.ToUpperInvariant() -replace '0','O' -replace '8','B' -replace '1','I' -replace '5','S') -replace '[^A-Z]','') }
            $want = Norm $value
            $hit = Get-ScreenText | Where-Object { $n = Norm $_.Text; $n -eq $want -or ($want.Length -gt 8 -and $n.Contains($want.Substring(0, 8))) } | Select-Object -First 1
            if (-not $hit) { "ABORT: text '$value' not on screen"; return }
            "ocr $value at $($hit.X),$($hit.Y)"
            Click ($hit.X + [int]($hit.W / 2)) ($hit.Y + [int]($hit.H / 2)) 2 4
        }
        'item'  {
            $e = Find-Item $value
            if (-not $e) { "ABORT: menu item '$value' not found"; return }
            $r = $e.Current.BoundingRectangle
            Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) 2 4
        }
    }
    Start-Sleep -Milliseconds 400
    "did $step"
}
