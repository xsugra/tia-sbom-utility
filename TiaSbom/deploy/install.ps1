<#
  Installs the TiaSbom Add-In for every TIA Portal version found on this PC. Safe to run repeatedly.

    .\install.ps1                 install from the packages next to this script or from local build output
    .\install.ps1 -Check          show what would happen, change nothing
    .\install.ps1 -Build          build the Add-In for the installed TIA first (development)
    .\install.ps1 -Uninstall      remove the Add-In and config.json

  Settings come from tiasbom.env next to this script. Run as administrator, or as SYSTEM from a GPO startup script.

  Exit codes: 0 done, 1 failed, 2 TIA found but no package for any of its versions,
              3 installed, but Gitea is not reachable from this PC (see install.log for the TS- code).
#>
[CmdletBinding()]
param(
    [string]$EnvFile,
    [string]$ConfigDir = (Join-Path $env:ProgramData 'TiaSbom'),
    [switch]$Check,
    [switch]$Build,
    [switch]$Uninstall,
    [switch]$SkipConnectionTest
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults.
if (-not $EnvFile) { $EnvFile = Join-Path $PSScriptRoot 'tiasbom.env' }

# TIA versions with their own Add-In build. Others are reported, never served with a package of another version.
$BuildableVersions = 17, 18, 19, 20, 21
$KnownKeys = 'TIA_ROOT', 'GITEA_BASE_URL', 'GITEA_OWNER', 'GITEA_BRANCH', 'GITEA_PRIVATE_REPOS',
             'GITEA_TIMEOUT_SECONDS', 'GITEA_TOKEN', 'OPENNESS_MEMBERS', 'ADDIN_SCOPE'
$OpennessGroup = 'Siemens TIA Openness'
$script:Warnings = 0
$script:ConnectionFailed = $false

function Write-Step([string]$Message, [string]$Level = 'INFO') {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1,-5} {2}' -f (Get-Date), $Level, $Message
    Write-Host $line
    if ($Level -eq 'WARN') { $script:Warnings++ }
    if ($Check) { return }
    try {
        New-Item -ItemType Directory -Force $ConfigDir | Out-Null
        Add-Content -LiteralPath (Join-Path $ConfigDir 'install.log') -Value $line -Encoding UTF8
    }
    catch { Write-Host "Cannot write install.log: $($_.Exception.Message)" }
}

# KEY=VALUE per line. A whole line can be a comment; a # inside a value belongs to the value, tokens may contain it.
function Read-EnvFile([string]$Path) {
    $values = @{}
    foreach ($raw in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }

        if ($line -notmatch '^([A-Za-z0-9_]+)\s*=\s*(.*)$') { throw "Invalid line in ${Path}: $line" }
        $key = $Matches[1].ToUpperInvariant()
        $value = $Matches[2].Trim()
        if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[-1] -eq '"') {
            $value = $value.Substring(1, $value.Length - 2)
        }
        if ($KnownKeys -notcontains $key -and $key -notmatch '^ADDIN_DIR_V\d+$') {
            Write-Step "Unknown setting $key in $Path" 'WARN'
        }
        $values[$key] = [Environment]::ExpandEnvironmentVariables($value)
    }
    return $values
}

function Get-TiaVersions([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    $versions = foreach ($folder in Get-ChildItem -LiteralPath $Root -Directory) {
        if ($folder.Name -match '^Portal V(\d+)$') { [int]$Matches[1] }
    }
    return @($versions | Sort-Object)
}

# V17 and V19 load from the installation folder. V21 has Corporate, System and User add-ins.
# ADDIN_SCOPE=corporate uses the folder for all users of the PC, documented by Siemens for V21:
# C:\ProgramData\Siemens\Automation\Portal V21\CorporateAddIns. Default stays the per-user folder, the verified one.
function Get-AddInDir([hashtable]$Settings, [int]$Version, [string]$TiaRoot) {
    $key = "ADDIN_DIR_V$Version"
    if ($Settings.ContainsKey($key) -and $Settings[$key]) { return $Settings[$key] }
    if ($Version -ge 21) {
        if ($Settings['ADDIN_SCOPE'] -eq 'corporate') {
            return Join-Path $env:ProgramData "Siemens\Automation\Portal V$Version\CorporateAddIns"
        }
        return Join-Path $env:APPDATA "Siemens\Automation\Portal V$Version\UserAddIns"
    }
    return Join-Path $TiaRoot "Portal V$Version\AddIns"
}

# Maps a failed web request to the codes of docs/11-chybove-hlasky.md.
function Get-ErrorCode($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($e) {
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
                    if ($status -eq 401) { return 'TS-AUTH-03' }
                    if ($status -eq 404) { return 'TS-GIT-01' }
                    if ($status -ge 500) { return 'TS-GIT-05' }
                    return "HTTP $status"
                }
            }
        }
        $e = $e.InnerException
    }
    return 'TS-UNK-01'
}

# The same checks as tiasbom --check, so a wrong address or a blocked port shows up at installation, not at the first export.
# A personal token cannot be checked here: it belongs to the user, not to the installing account.
function Test-GiteaConnection([hashtable]$Settings) {
    if ($Check -or $SkipConnectionTest) { return }
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    $base = $Settings['GITEA_BASE_URL'].TrimEnd('/') + '/api/v1'
    $timeout = 15

    try {
        $answer = Invoke-RestMethod -Uri "$base/version" -TimeoutSec $timeout -UseBasicParsing
        if (-not $answer.version) {
            Write-Step "Connection test TS-API-01: $base/version does not answer like Gitea. Check GITEA_BASE_URL." 'WARN'
            $script:ConnectionFailed = $true
            return
        }
        Write-Step "Connection test: Gitea $($answer.version) reachable at $($Settings['GITEA_BASE_URL'])"
    }
    catch {
        Write-Step "Connection test $(Get-ErrorCode $_): $($Settings['GITEA_BASE_URL']) not reachable from this PC: $($_.Exception.Message)" 'WARN'
        $script:ConnectionFailed = $true
        return
    }

    $token = $Settings['GITEA_TOKEN']
    if (-not $token) {
        Write-Step 'Connection test: no service token, each user enters a personal token at the first export'
        return
    }
    $headers = @{ Authorization = "token $token" }
    try {
        $user = Invoke-RestMethod -Uri "$base/user" -Headers $headers -TimeoutSec $timeout -UseBasicParsing
        $null = Invoke-RestMethod -Uri "$base/orgs/$([Uri]::EscapeDataString($Settings['GITEA_OWNER']))" -Headers $headers -TimeoutSec $timeout -UseBasicParsing
        Write-Step "Connection test: service token of $($user.login) accepted, organization $($Settings['GITEA_OWNER']) found"
    }
    catch {
        Write-Step "Connection test $(Get-ErrorCode $_): service token or organization $($Settings['GITEA_OWNER']) rejected: $($_.Exception.Message)" 'WARN'
        $script:ConnectionFailed = $true
    }
}

function Find-Package([int]$Version) {
    $name = "TiaSbom.V$Version.addin"
    $candidates = @(
        (Join-Path $PSScriptRoot "addins\$name"),
        (Join-Path $PSScriptRoot "..\src\TiaSbom.AddIn\bin\V$Version\Release\net48\$name")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function Install-AddIn([int]$Version, [string]$Package, [string]$Directory) {
    $target = Join-Path $Directory "TiaSbom.V$Version.addin"
    $sameFile = (Test-Path -LiteralPath $target) -and
        ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $Package).Hash)

    # An older file name would show up in TIA as a second Add-In. Names are compared, a full path may be spelled differently.
    $stale = @(Get-ChildItem -LiteralPath $Directory -Filter 'TiaSbom*.addin' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne "TiaSbom.V$Version.addin" })

    if ($Check) {
        foreach ($file in $stale) { Write-Step "V${Version}: would remove $($file.FullName)" }
        if ($sameFile) { Write-Step "V${Version}: already up to date, $target" }
        else { Write-Step "V${Version}: would copy $Package to $target" }
        return
    }

    New-Item -ItemType Directory -Force $Directory | Out-Null
    foreach ($file in $stale) {
        Remove-Item -LiteralPath $file.FullName -Force
        Write-Step "V${Version}: removed $($file.FullName)"
    }
    if ($sameFile) {
        Write-Step "V${Version}: already up to date, $target"
    }
    else {
        Copy-Item -LiteralPath $Package -Destination $target -Force
        Write-Step "V${Version}: installed $target"
    }
}

function Write-Config([hashtable]$Settings) {
    foreach ($key in 'GITEA_BASE_URL', 'GITEA_OWNER') {
        if (-not $Settings[$key]) { throw "$key must be set in $EnvFile" }
    }
    if ($Settings['GITEA_BASE_URL'] -notmatch '^https?://') { throw 'GITEA_BASE_URL must start with http:// or https://' }
    if ($Settings['GITEA_BASE_URL'] -match '^http://') { Write-Step 'GITEA_BASE_URL uses plain HTTP, the token travels unencrypted' 'WARN' }

    $config = [ordered]@{
        BaseUrl        = $Settings['GITEA_BASE_URL'].TrimEnd('/')
        Owner          = $Settings['GITEA_OWNER']
        Branch         = if ($Settings['GITEA_BRANCH']) { $Settings['GITEA_BRANCH'] } else { 'main' }
        PrivateRepos   = $Settings['GITEA_PRIVATE_REPOS'] -eq 'true'
        TimeoutSeconds = if ($Settings['GITEA_TIMEOUT_SECONDS']) { [int]$Settings['GITEA_TIMEOUT_SECONDS'] } else { 60 }
    }
    $hasToken = [bool]$Settings['GITEA_TOKEN']
    if ($hasToken) { $config['Token'] = $Settings['GITEA_TOKEN'] }

    $path = Join-Path $ConfigDir 'config.json'
    $summary = "$($config.BaseUrl), owner $($config.Owner), service token: $(if ($hasToken) { 'yes' } else { 'no' })"
    if ($Check) { Write-Step "Would write $path ($summary)"; return }

    New-Item -ItemType Directory -Force $ConfigDir | Out-Null
    $config | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8

    if ($hasToken) {
        # The Add-In runs as the user, so users must read this file. They must not be able to change it.
        $acl = New-Object System.Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @(
                @('S-1-5-18', 'FullControl'),       # SYSTEM
                @('S-1-5-32-544', 'FullControl'),   # Administrators
                @('S-1-5-32-545', 'Read'))) {       # Users
            $sid = New-Object System.Security.Principal.SecurityIdentifier($rule[0])
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid, $rule[1], 'Allow')))
        }
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    Write-Step "Wrote $path ($summary)"
}

# Copies tiasbom.exe and check.cmd to %ProgramData%\TiaSbom, so every user can check the connection later.
function Install-Tools {
    $source = Join-Path $PSScriptRoot 'tools'
    if (-not (Test-Path -LiteralPath (Join-Path $source 'tiasbom.exe'))) { return }
    $target = Join-Path $ConfigDir 'tools'
    if ($Check) { Write-Step "Would copy tools to $target"; return }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $target -Force
    $launcher = Join-Path $PSScriptRoot 'check.cmd'
    if (Test-Path -LiteralPath $launcher) { Copy-Item -LiteralPath $launcher -Destination $ConfigDir -Force }
    Write-Step "Tools: $target, users run $(Join-Path $ConfigDir 'check.cmd')"
}

function Add-OpennessMembers([string]$Members) {
    if (-not $Members) { return }
    if (-not (Get-LocalGroup -Name $OpennessGroup -ErrorAction SilentlyContinue)) {
        Write-Step "Group '$OpennessGroup' does not exist, members not added" 'WARN'
        return
    }
    foreach ($member in ($Members -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
        if ($Check) { Write-Step "Would add $member to '$OpennessGroup'"; continue }
        try {
            Add-LocalGroupMember -Group $OpennessGroup -Member $member
            Write-Step "Added $member to '$OpennessGroup', takes effect at next sign-in"
        }
        catch [Microsoft.PowerShell.Commands.MemberExistsException] {
            Write-Step "$member is already in '$OpennessGroup'"
        }
    }
}

function Uninstall-All([hashtable]$Settings, [int[]]$Versions, [string]$TiaRoot) {
    foreach ($version in $Versions) {
        $directory = Get-AddInDir $Settings $version $TiaRoot
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter 'TiaSbom*.addin' -ErrorAction SilentlyContinue)) {
            if ($Check) { Write-Step "Would remove $($file.FullName)"; continue }
            Remove-Item -LiteralPath $file.FullName -Force
            Write-Step "Removed $($file.FullName)"
        }
    }
    foreach ($item in (Join-Path $ConfigDir 'tools'), (Join-Path $ConfigDir 'check.cmd')) {
        if (-not (Test-Path -LiteralPath $item)) { continue }
        if ($Check) { Write-Step "Would remove $item" }
        else { Remove-Item -LiteralPath $item -Recurse -Force; Write-Step "Removed $item" }
    }
    $config = Join-Path $ConfigDir 'config.json'
    if (Test-Path -LiteralPath $config) {
        if ($Check) { Write-Step "Would remove $config" }
        else { Remove-Item -LiteralPath $config -Force; Write-Step "Removed $config" }
    }
}

try {
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        throw "Settings file not found: $EnvFile. Copy tiasbom.env.example to tiasbom.env and fill it in."
    }
    $settings = Read-EnvFile $EnvFile
    $tiaRoot = if ($settings['TIA_ROOT']) { $settings['TIA_ROOT'] } else { 'C:\Program Files\Siemens\Automation' }
    $versions = Get-TiaVersions $tiaRoot
    Write-Step "$(if ($Check) { 'Check' } elseif ($Uninstall) { 'Uninstall' } else { 'Install' }) on $env:COMPUTERNAME, TIA: $(if ($versions) { ($versions | ForEach-Object { "V$_" }) -join ', ' } else { 'none' })"

    if ($Uninstall) {
        Uninstall-All $settings $versions $tiaRoot
        exit 0
    }
    if (-not $versions) {
        Write-Step 'No TIA Portal on this PC, nothing to do'
        exit 0
    }

    if ($Build -and -not $Check) {
        foreach ($version in $versions | Where-Object { $BuildableVersions -contains $_ }) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot '..\build.ps1') -Api "V$version" -AddIn
            if ($LASTEXITCODE -ne 0) { throw "Build for V$version failed" }
        }
    }

    $isSystem = [Security.Principal.WindowsIdentity]::GetCurrent().IsSystem
    $installed = 0
    foreach ($version in $versions) {
        $package = Find-Package $version
        if (-not $package) {
            Write-Step "V${version}: no package TiaSbom.V$version.addin, skipped" 'WARN'
            continue
        }
        $directory = Get-AddInDir $settings $version $tiaRoot
        if ($isSystem -and $directory -like "$env:APPDATA*") {
            Write-Step "V${version}: per-user folder is not reachable as SYSTEM, set ADDIN_DIR_V$version" 'WARN'
            continue
        }
        Install-AddIn $version $package $directory
        $installed++
    }

    Write-Config $settings
    Install-Tools
    Add-OpennessMembers $settings['OPENNESS_MEMBERS']
    Test-GiteaConnection $settings

    if (Get-Process -Name 'Siemens.Automation.Portal' -ErrorAction SilentlyContinue) {
        Write-Step 'TIA Portal is running, restart it to load the Add-In' 'WARN'
    }
    Write-Step "Finished: $installed of $($versions.Count) TIA versions, $($script:Warnings) warnings"
    if ($installed -eq 0) { exit 2 }
    if ($script:ConnectionFailed) { exit 3 }
    exit 0
}
catch {
    Write-Step $_.Exception.Message 'ERROR'
    exit 1
}
