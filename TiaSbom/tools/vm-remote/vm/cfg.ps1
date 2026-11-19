# Test helper: temporarily points config.json elsewhere. -Restore puts the original back.
param([string]$Url, [switch]$Restore)
$cfg = "$env:ProgramData\TiaSbom\config.json"
$backup = 'C:\TiaSbomTest\backup\config.json.orig'
if ($Restore) { Copy-Item $backup $cfg -Force; "restored: " + (Get-Content $cfg -Raw | ConvertFrom-Json).BaseUrl; return }
if (-not (Test-Path $backup)) { New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Copy-Item $cfg $backup }
$c = Get-Content $backup -Raw | ConvertFrom-Json
$c.BaseUrl = $Url
$c | ConvertTo-Json | Set-Content $cfg -Encoding UTF8
"config BaseUrl = $Url"
