<#
  Builds tiasbom.exe for one Openness API, optionally also the Add-In.

    .\build.ps1 -Api V17                  build on a machine with TIA V17
    .\build.ps1 -Api V17 -Portal V19      use the V17 API shipped with TIA V19
    .\build.ps1 -Api V21 -AddIn           also build and package the Add-In

  API V16 runs on TIA V16-V19, V17 on V17-V20, V21 on V21 only.
#>
param(
    [ValidateSet('V16', 'V17', 'V21')][string]$Api = 'V21',
    [string]$Portal = $Api,
    [string]$Configuration = 'Release',
    [switch]$AddIn
)

$ErrorActionPreference = 'Stop'
$framework = if ($Api -eq 'V16') { 'net462' } else { 'net48' }
$props = @("-p:TiaApi=$Api", "-p:TiaPortal=$Portal", '-c', $Configuration)

dotnet build "$PSScriptRoot\src\TiaSbom.Cli" @props
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "CLI: $PSScriptRoot\src\TiaSbom.Cli\bin\$Api\$Configuration\$framework\tiasbom.exe"

if (-not $AddIn) { exit 0 }

# The publisher configuration is validated only against the V21 schema.
if ($Api -ne 'V21') {
    throw "Add-In packaging for API $Api needs its publisher XSD from PublicAPI\$Api. Only V21 is verified."
}

dotnet build "$PSScriptRoot\src\TiaSbom.AddIn" @props
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = "$PSScriptRoot\src\TiaSbom.AddIn\bin\$Api\$Configuration\$framework"
$publisher = "C:\Program Files\Siemens\Automation\Portal $Portal\PublicAPI\$Api\Siemens.Engineering.AddIn.Publisher.exe"
Push-Location $output
try {
    & $publisher -f AddInPublisherConfig.xml -o "TiaSbom.$Api.addin" -c
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally { Pop-Location }
Write-Host "Add-In: $output\TiaSbom.$Api.addin"
