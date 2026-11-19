# Runs "Export SBOM to Gitea" on the project root in TIA (menu items found by name) and takes a screenshot after $Wait s.
param([Parameter(Mandatory)][string]$Name, [int]$Wait = 20)
$S = $PSScriptRoot
$seq = "keys:{ESC};wait:1;click:90,183;wait:1;right:90,183;wait:2;ocr:SBOM;wait:2;ocr:Export SBOM to Gitea;wait:$Wait;shot:$Name"
& "$S\vmui.ps1" -Name click.ps1 -TaskArgs "-Do `"$seq`"" -TimeoutSeconds ($Wait + 90)
scp -i "$HOME\.ssh\tia_vm_ed25519" -o BatchMode=yes -o LogLevel=ERROR -q "$($env:TIASBOM_VM_USER)@$($env:TIASBOM_VM_HOST):C:/TiaSbomTest/shots/$Name.png" "$S\shots\"
