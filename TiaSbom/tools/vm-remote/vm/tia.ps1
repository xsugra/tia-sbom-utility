# Starts or closes TIA Portal in the interactive desktop. -Action close only closes when no project is open (title "Siemens").
param([ValidateSet('start', 'close', 'status')][string]$Action = 'status', [switch]$WithProject)
$exe = 'C:\Program Files\Siemens\Automation\Portal V21\Bin\Siemens.Automation.Portal.exe'
$p = Get-Process Siemens.Automation.Portal -ErrorAction SilentlyContinue
switch ($Action) {
    'status' { if ($p) { $p | Select-Object Id, MainWindowTitle | Format-Table -AutoSize | Out-String } else { 'not running' } }
    'close' {
        if (-not $p) { 'not running'; return }
        if ($p.MainWindowTitle -ne 'Siemens' -and -not $WithProject) { "REFUSED: a project may be open: '$($p.MainWindowTitle)'"; return }
        $p.CloseMainWindow() | Out-Null
        if ($p.WaitForExit(60000)) { 'closed' } else { 'still running after 60 s' }
    }
    'start' {
        if ($p) { 'already running'; return }
        Start-Process $exe
        $deadline = (Get-Date).AddMinutes(4)
        do { Start-Sleep 3; $p = Get-Process Siemens.Automation.Portal -ErrorAction SilentlyContinue | Where-Object MainWindowTitle } while (-not $p -and (Get-Date) -lt $deadline)
        Start-Sleep 15
        if ($p) { "started: $($p.MainWindowTitle)" } else { 'no window after 4 min' }
    }
}
