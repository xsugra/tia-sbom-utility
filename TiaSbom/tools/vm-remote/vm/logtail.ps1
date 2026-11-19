param([int]$Lines = 8)
Get-Content "$env:APPDATA\TiaSbom\addin.log" -Tail $Lines | ForEach-Object { if ($_.Length -gt 240) { $_.Substring(0, 240) + '...' } else { $_ } }
