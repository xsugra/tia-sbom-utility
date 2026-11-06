<#
  Updates a development VM from the newest ZIP in Downloads: unpack, find Gitea, build, install, report.

    update-vm.cmd                 run on the VM after downloading the ZIP
    update-vm.cmd -Report         show the logs to send back after testing

  The first run also copies itself to C:\Code, so later runs do not depend on an unpacked ZIP.
  Development only. Installing on company PCs is install.ps1, see docs/09-instalacia.md.
#>
[CmdletBinding()]
param(
    [string]$GiteaHost,
    [string]$CodeRoot = 'C:\Code',
    [string]$DownloadFolder = (Join-Path $env:USERPROFILE 'Downloads'),
    [int]$MaxZipAgeMinutes = 60,
    [switch]$AnyZip,
    [switch]$NoBuild,
    [switch]$Report
)

$ErrorActionPreference = 'Stop'
$GiteaPort = 3000

function Show-Logs {
    foreach ($log in (Join-Path $env:ProgramData 'TiaSbom\install.log'), (Join-Path $env:APPDATA 'TiaSbom\addin.log')) {
        Write-Host "--- $log"
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 60 } else { Write-Host 'not found' }
    }
}

# An old ZIP looks like a successful update, so a stale one has to stop the run.
function Get-NewestZip {
    $zip = Get-ChildItem -Path (Join-Path $DownloadFolder 'tia-sbom-utility*.zip') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $zip) { throw "No tia-sbom-utility*.zip in $DownloadFolder. Download it in the browser first." }

    $age = [int]((Get-Date) - $zip.LastWriteTime).TotalMinutes
    Write-Host "ZIP: $($zip.Name), $($zip.LastWriteTime), $age min old"
    if ($age -gt $MaxZipAgeMinutes -and -not $AnyZip) {
        throw "The newest ZIP is $age minutes old. Download it again, or use -AnyZip if it is the right one."
    }
    return $zip
}

function Expand-Zip($Zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip.FullName)
    try { $top = ($archive.Entries[0].FullName -split '[/\\]')[0] }
    finally { $archive.Dispose() }

    Expand-Archive -LiteralPath $Zip.FullName -DestinationPath $CodeRoot -Force
    return Join-Path $CodeRoot "$top\TiaSbom"
}

function Get-AddInVersion([string]$Solution) {
    $config = Get-ChildItem -LiteralPath (Join-Path $Solution 'src\TiaSbom.AddIn') -Filter 'AddInPublisherConfig.*.xml' | Select-Object -First 1
    return ([xml](Get-Content -LiteralPath $config.FullName -Raw)).PackageConfiguration.Product.Version
}

# The Default Switch address changes after a reboot of the laptop. It is the default gateway of the VM.
function Find-Gitea {
    $candidates = if ($GiteaHost) { @($GiteaHost) } else {
        @(Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway } |
            ForEach-Object { $_.IPv4DefaultGateway.NextHop } | Select-Object -Unique)
    }
    foreach ($candidate in $candidates) {
        if ((Test-NetConnection $candidate -Port $GiteaPort -WarningAction SilentlyContinue).TcpTestSucceeded) { return $candidate }
    }
    throw "Gitea is not reachable on port $GiteaPort at: $($candidates -join ', '). Is WSL running on the laptop? See docs/03-gitea-server.md."
}

function Set-EnvFile([string]$Solution, [string]$Address) {
    $path = Join-Path $Solution 'deploy\tiasbom.env'
    $lines = if (Test-Path -LiteralPath $path) { @(Get-Content -LiteralPath $path) } else {
        @('GITEA_BASE_URL=', 'GITEA_OWNER=sbom', 'GITEA_BRANCH=main', 'GITEA_TIMEOUT_SECONDS=10')
    }
    $lines = @($lines | ForEach-Object { if ($_ -like 'GITEA_BASE_URL=*') { "GITEA_BASE_URL=http://${Address}:$GiteaPort" } else { $_ } })
    $lines | Set-Content -LiteralPath $path -Encoding UTF8
}

# Copies the newest script next to the code, a run from there replaces itself for the next time.
function Save-StableCopy([string]$Solution) {
    foreach ($file in 'update-vm.ps1', 'update-vm.cmd') {
        $source = Join-Path $Solution "deploy\$file"
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $CodeRoot -Force }
    }
}

try {
    if ($Report) { Show-Logs; exit 0 }

    if (Get-Process -Name 'Siemens.Automation.Portal' -ErrorAction SilentlyContinue) {
        throw 'TIA Portal is running. Close it and run this again.'
    }

    $zip = Get-NewestZip
    $solution = Expand-Zip $zip
    $version = Get-AddInVersion $solution
    Write-Host "Code: $solution, Add-In version $version"

    $address = Find-Gitea
    Write-Host "Gitea: http://${address}:$GiteaPort"
    Set-EnvFile $solution $address
    Save-StableCopy $solution

    if (-not $NoBuild) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $solution 'deploy\install.ps1') -Build
        if ($LASTEXITCODE -ne 0) { throw "Installation failed with exit code $LASTEXITCODE. See the output above." }
    }

    Write-Host ''
    Write-Host "DONE. Open TIA, check version $version in Add-ins > Details and activate the Add-In." -ForegroundColor Green
    Write-Host 'After testing run: update-vm.cmd -Report'
    exit 0
}
catch {
    Write-Host ''
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
