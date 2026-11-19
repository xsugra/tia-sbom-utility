# Runs a script in the interactive desktop of the signed-in user (session 1) through a one-shot scheduled task.
# SSH sessions cannot see the desktop, take screenshots or read the user's DPAPI token. Output goes to <script>.out.
# Arguments come from <script>.args (one PowerShell argument string), so no quoting passes through Task Scheduler.
param([Parameter(Mandatory)][string]$Name, [int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
$run = 'C:\TiaSbomTest\run'
$script = Join-Path $run $Name
$out = "$script.out"
Remove-Item $out, "$out.done" -ErrorAction SilentlyContinue
$wrapper = Join-Path $run 'ui-wrap.ps1'
@'
param([string]$Target)
$out = "$Target.out"
$argText = if (Test-Path "$Target.args") { Get-Content "$Target.args" -Raw } else { '' }
try { Invoke-Expression "& '$Target' $argText" *> $out } catch { "WRAPPER ERROR: $_" | Out-File $out -Append }
'DONE' | Out-File "$out.done"
'@ | Set-Content $wrapper -Encoding UTF8
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$wrapper`" -Target `"$script`""
$principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$env:USERNAME" -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskName TiaSbomUi -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName TiaSbomUi
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path "$out.done") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-Path $out) { Get-Content $out }
if (-not (Test-Path "$out.done")) { "TIMEOUT after $TimeoutSeconds s"; exit 2 }
