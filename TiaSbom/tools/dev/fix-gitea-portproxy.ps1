# LAPTOP. Moves the Gitea portproxy and its firewall rule to the current Default Switch address.
# Listens only on the Hyper-V Default Switch, never on 0.0.0.0: no other PC in the company network sees port 3000.
$ErrorActionPreference = 'Stop'
$a = Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' -AddressFamily IPv4
$ip = $a.IPAddress
$net = (Get-NetRoute -InterfaceAlias 'vEthernet (Default Switch)' -AddressFamily IPv4 | Where-Object { $_.DestinationPrefix -like "*/$($a.PrefixLength)" } | Select-Object -First 1).DestinationPrefix
Write-Host "Default Switch: $ip, subnet $net"
foreach ($line in (netsh interface portproxy show v4tov4) -match '^\s*\d+\.\d+\.\d+\.\d+\s+3000\s') {
    $old = ($line.Trim() -split '\s+')[0]
    netsh interface portproxy delete v4tov4 listenaddress=$old listenport=3000 | Out-Null
    Write-Host "Removed old portproxy $old`:3000"
}
netsh interface portproxy add v4tov4 listenaddress=$ip listenport=3000 connectaddress=127.0.0.1 connectport=3000
Remove-NetFirewallRule -DisplayName 'Gitea for TIA VM' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'Gitea for TIA VM' -Direction Inbound -Protocol TCP -LocalAddress $ip -RemoteAddress $net -LocalPort 3000 -Action Allow | Out-Null
netsh interface portproxy show v4tov4
$ok = (Test-NetConnection $ip -Port 3000 -WarningAction SilentlyContinue).TcpTestSucceeded
if ($ok) { Write-Host "OK: Gitea answers on $ip`:3000" -ForegroundColor Green } else { Write-Host "FAILED: nothing answers on $ip`:3000. Is WSL running?" -ForegroundColor Red }
