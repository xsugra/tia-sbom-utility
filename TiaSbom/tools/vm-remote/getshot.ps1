# Takes a VM screenshot (optional marks) and copies it to the local shots folder.
param([Parameter(Mandatory)][string]$Name, [string]$Marks = '')
$S = $PSScriptRoot
$args1 = "-Path C:\TiaSbomTest\shots\$Name.png"
if ($Marks) { $args1 += " -Marks '$Marks'" }
& "$S\vmui.ps1" -Name shot.ps1 -TaskArgs $args1 -TimeoutSeconds 60 | Out-Null
New-Item -ItemType Directory -Force "$S\shots" | Out-Null
scp -i "$HOME\.ssh\tia_vm_ed25519" -o BatchMode=yes -o LogLevel=ERROR -q "$($env:TIASBOM_VM_USER)@$($env:TIASBOM_VM_HOST):C:/TiaSbomTest/shots/$Name.png" "$S\shots\"
"$S\shots\$Name.png"
