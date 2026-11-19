# Copies scripts to the VM and runs one of them in the interactive desktop. -TaskArgs is passed through a .args file.
param([Parameter(Mandatory)][string]$Name, [string]$TaskArgs = '', [int]$TimeoutSeconds = 300, [string]$VmHost = $env:TIASBOM_VM_HOST)
$dir = Join-Path $PSScriptRoot 'vm'
Set-Content (Join-Path $dir "$Name.args") $TaskArgs -Encoding UTF8
$opts = @('-i', "$HOME\.ssh\tia_vm_ed25519", '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-o', 'LogLevel=ERROR')
& ssh @opts "$($env:TIASBOM_VM_USER)@$VmHost" 'if not exist C:\TiaSbomTest\run mkdir C:\TiaSbomTest\run' | Out-Null
& scp @opts -q (Get-ChildItem $dir -File).FullName "$($env:TIASBOM_VM_USER)@${VmHost}:C:/TiaSbomTest/run/"
& ssh @opts "$($env:TIASBOM_VM_USER)@$VmHost" "powershell -NoProfile -ExecutionPolicy Bypass -File C:\TiaSbomTest\run\ui.ps1 -Name $Name -TimeoutSeconds $TimeoutSeconds"
