<#
  Installs the newest TiaSbom release from Gitea with the settings kept on this PC. Run it through update.cmd.

    update.cmd                      install the latest release when it is newer than the installed version
    update.cmd -Check               download and verify it, show what install.ps1 would do, change nothing
    update.cmd -Version 0.2.12      install exactly this release, also an older one (rollback)
    update.cmd -Force               install the latest release even when it is already installed

  Needs %ProgramData%\TiaSbom\setup from install.ps1, with GITEA_UPDATE_REPO (owner/repository) and the encrypted
  GITEA_UPDATE_TOKEN, a read-only token that sees only the release repository.
  The release must have one asset TiaSbom-x.y.z.zip from package.ps1 and the line "SHA256: <hash>" in its description.
  The downloaded package is installed only when its hash matches that line.

  Exit codes: 0 done or already up to date, 1 failed (TS- code in install.log), 4 TIA Portal is running,
              2 and 3 as in install.ps1.
#>
[CmdletBinding()]
param(
    [string]$ConfigDir = (Join-Path $env:ProgramData 'TiaSbom'),
    [string]$Version,
    [switch]$Check,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$SetupDir = Join-Path $ConfigDir 'setup'
$EnvFile = Join-Path $SetupDir 'tiasbom.env'

function Write-Step([string]$Message, [string]$Level = 'INFO') {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1,-5} Update: {2}' -f (Get-Date), $Level, $Message
    Write-Host $line
    if ($Check) { return }
    try { Add-Content -LiteralPath (Join-Path $ConfigDir 'install.log') -Value $line -Encoding UTF8 } catch { }
}

# An error the administrator can act on. The code is in docs/11-chybove-hlasky.md.
function Stop-Update([string]$Code, [string]$Message) {
    throw (New-Object System.Exception("$Code $Message"))
}

function Read-Settings {
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        Stop-Update 'TS-UPD-01' "No settings in $SetupDir. Install once from the USB stick with install.cmd (0.2.13 or newer)."
    }
    $values = @{}
    foreach ($raw in Get-Content -LiteralPath $EnvFile -Encoding UTF8) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
        if ($line -match '^([A-Za-z0-9_]+)\s*=\s*(.*)$') { $values[$Matches[1].ToUpperInvariant()] = $Matches[2].Trim() }
    }
    if (-not $values['GITEA_BASE_URL']) { Stop-Update 'TS-UPD-01' "GITEA_BASE_URL is missing in $EnvFile." }
    if ($values['GITEA_UPDATE_REPO'] -notmatch '^[^/\s]+/[^/\s]+$') {
        Stop-Update 'TS-UPD-01' "GITEA_UPDATE_REPO (owner/repository) is not set. Add it to tiasbom.env on the USB stick and run install.cmd again."
    }
    return $values
}

function Read-UpdateToken {
    $file = Join-Path $SetupDir 'update-token.bin'
    if (-not (Test-Path -LiteralPath $file)) {
        Stop-Update 'TS-UPD-01' "No update token on this PC. Add GITEA_UPDATE_TOKEN to tiasbom.env on the USB stick and run install.cmd again."
    }
    Add-Type -AssemblyName System.Security
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file), $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    return [Text.Encoding]::UTF8.GetString($bytes)
}

# Same mapping as install.ps1, with the codes of the update where the cause differs.
function Get-ErrorCode($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($e) {
        if ($e.Message -match '^(TS-[A-Z]+-\d\d) ') { return $Matches[1] }
        if ($e -is [System.Security.Authentication.AuthenticationException]) { return 'TS-TLS-01' }
        if ($e -is [System.Net.WebException]) {
            switch ($e.Status) {
                'NameResolutionFailure' { return 'TS-NET-01' }
                'TrustFailure' { return 'TS-TLS-01' }
                'SecureChannelFailure' { return 'TS-TLS-01' }
                'Timeout' { return 'TS-NET-04' }
                'ConnectFailure' {
                    $socket = $e.InnerException
                    while ($socket -and $socket -isnot [System.Net.Sockets.SocketException]) { $socket = $socket.InnerException }
                    if ($socket -and $socket.SocketErrorCode -eq 'ConnectionRefused') { return 'TS-NET-02' }
                    return 'TS-NET-03'
                }
                'ProtocolError' {
                    $status = [int]$e.Response.StatusCode
                    if ($status -eq 401 -or $status -eq 403) { return 'TS-UPD-06' }
                    if ($status -eq 404) { return 'TS-UPD-02' }
                    if ($status -ge 500) { return 'TS-GIT-05' }
                    return "HTTP $status"
                }
            }
        }
        $e = $e.InnerException
    }
    return 'TS-UNK-01'
}

function ConvertTo-Version([string]$Text) {
    $clean = ($Text -replace '^[vV]', '') -replace '-.*$', ''
    $parsed = $null
    if ([Version]::TryParse($clean, [ref]$parsed)) { return $parsed }
    return $null
}

$work = $null
try {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { Stop-Update 'TS-SEC-01' 'Run update.cmd as administrator (right click, Run as administrator).' }

    $settings = Read-Settings
    $token = Read-UpdateToken
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    $base = [Uri]($settings['GITEA_BASE_URL'].TrimEnd('/') + '/')
    $timeout = if ($settings['GITEA_TIMEOUT_SECONDS']) { [int]$settings['GITEA_TIMEOUT_SECONDS'] } else { 60 }
    $headers = @{ Authorization = "token $token" }
    $repo = $settings['GITEA_UPDATE_REPO']

    $versionFile = Join-Path $ConfigDir 'version.txt'
    $installedText = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw).Trim() } else { $null }
    $installed = if ($installedText) { ConvertTo-Version $installedText } else { $null }

    $api = [Uri]::new($base, "api/v1/repos/$repo/releases/")
    $releasePath = if ($Version) { 'tags/v' + ($Version -replace '^[vV]', '') } else { 'latest' }
    $releaseUri = [Uri]::new($api, $releasePath)
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -TimeoutSec $timeout -UseBasicParsing
    $releaseVersion = ConvertTo-Version $release.tag_name
    if (-not $releaseVersion) { Stop-Update 'TS-UPD-02' "Release tag '$($release.tag_name)' in $repo is not a version like v0.2.13." }
    Write-Step "Installed: $(if ($installedText) { $installedText } else { 'unknown' }), release in ${repo}: $($release.tag_name)"

    if ($installed -and -not $Version -and -not $Force) {
        if ($releaseVersion -le $installed) {
            Write-Step "Already up to date, nothing to do$(if ($releaseVersion -lt $installed) { ' (the release is older, use -Version to go back)' })"
            exit 0
        }
    }

    $assets = @($release.assets | Where-Object { $_.name -match '^TiaSbom-.+\.zip$' })
    if ($assets.Count -ne 1) { Stop-Update 'TS-UPD-02' "Release $($release.tag_name) must have exactly one asset TiaSbom-x.y.z.zip, it has $($assets.Count)." }
    $asset = $assets[0]
    if ("$($release.body)" -notmatch '(?im)^\s*SHA256:\s*([0-9a-f]{64})\s*$') {
        Stop-Update 'TS-UPD-03' "The description of release $($release.tag_name) has no line 'SHA256: <hash>'. Nothing was installed."
    }
    $expected = $Matches[1].ToUpperInvariant()

    if (-not $Check -and (Get-Process -Name 'Siemens.Automation.Portal' -ErrorAction SilentlyContinue)) {
        Write-Step 'TS-UPD-04 TIA Portal is running. Close it and run update.cmd again. Nothing was changed.' 'ERROR'
        exit 4
    }

    # Download from the configured address, whatever ROOT_URL Gitea uses in browser_download_url.
    $download = [Uri]::new($base, ([Uri]$asset.browser_download_url).AbsolutePath.TrimStart('/'))
    $work = Join-Path $env:TEMP ('TiaSbomUpdate-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work | Out-Null
    $zip = Join-Path $work $asset.name
    Invoke-WebRequest -Uri $download -Headers $headers -OutFile $zip -TimeoutSec ([Math]::Max($timeout, 120)) -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        Stop-Update 'TS-UPD-03' "$($asset.name) has SHA256 $actual, the release says $expected. Nothing was installed."
    }
    Write-Step "Downloaded $($asset.name), SHA256 matches the release"

    $package = Join-Path $work 'package'
    Expand-Archive -LiteralPath $zip -DestinationPath $package
    $installer = Join-Path $package 'install.ps1'
    if (-not (Test-Path -LiteralPath $installer)) { Stop-Update 'TS-UPD-05' "$($asset.name) has no install.ps1 in its root. Create it with package.ps1." }

    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer, '-EnvFile', $EnvFile, '-ConfigDir', $ConfigDir)
    if ($Check) { $arguments += '-Check' }
    & powershell.exe @arguments
    $code = $LASTEXITCODE
    if ($Check) { Write-Step "Check finished, release $($release.tag_name) is ready to install"; exit $code }
    if ($code -eq 0 -or $code -eq 3) {
        Write-Step "Release $($release.tag_name) installed. Every user must activate the Add-In in TIA again (Add-Ins, Status, Activate)."
    }
    else { Write-Step "install.ps1 of $($release.tag_name) ended with code $code, see the lines above" 'ERROR' }
    exit $code
}
catch {
    $code = Get-ErrorCode $_
    $text = $_.Exception.Message -replace '^TS-[A-Z]+-\d\d ', ''
    if ($_.Exception -is [System.Net.WebException]) {
        $hint = switch ($code) {
            'TS-UPD-02' { "No release $(if ($Version) { "v$Version " })in '$repo', or the update token cannot see the repository. " }
            'TS-UPD-06' { 'Gitea rejected the update token. ' }
            default { '' }
        }
        $text = $hint + $text
    }
    Write-Step "$code $text" 'ERROR'
    exit 1
}
finally {
    if ($work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
