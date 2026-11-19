# Runs a local PowerShell script on the TIA VM over SSH: copies it to C:\TiaSbomTest\run and executes it there.
param([Parameter(Mandatory)][string]$Script, [string]$VmHost = $env:TIASBOM_VM_HOST, [string[]]$Arguments = @())
$key = "$HOME\.ssh\tia_vm_ed25519"
$opts = @('-i', $key, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-o', 'LogLevel=ERROR')
$name = Split-Path $Script -Leaf
& ssh @opts "$($env:TIASBOM_VM_USER)@$VmHost" 'if not exist C:\TiaSbomTest\run mkdir C:\TiaSbomTest\run' | Out-Null
& scp @opts -q $Script "$($env:TIASBOM_VM_USER)@${VmHost}:C:/TiaSbomTest/run/$name"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }
& ssh @opts "$($env:TIASBOM_VM_USER)@$VmHost" "powershell -NoProfile -ExecutionPolicy Bypass -File C:\TiaSbomTest\run\$name $($Arguments -join ' ')"
exit $LASTEXITCODE
