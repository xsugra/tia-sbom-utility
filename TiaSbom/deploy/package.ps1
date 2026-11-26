<#
  Collects the built Add-Ins and the installer into dist\TiaSbom-<version>, plus a ZIP of it.

    .\deploy\package.ps1

  Each Add-In can only be built on a PC with its TIA version. Run this on each build machine and copy the files
  from addins\ into one folder, or add a package built elsewhere with -AddInFolder.
  The ZIP is also the asset of a Gitea release for update.cmd; the SHA256 line it prints goes into the release description.
#>
param(
    [string]$AddInFolder,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $root 'dist' }
$addInSource = Join-Path $root 'src\TiaSbom.AddIn'

$config = Get-ChildItem -LiteralPath $addInSource -Filter 'AddInPublisherConfig.*.xml' | Select-Object -First 1
$version = ([xml](Get-Content -LiteralPath $config.FullName -Raw)).PackageConfiguration.Product.Version

$packages = @(Get-ChildItem -Path (Join-Path $addInSource 'bin\V*\Release\net*\TiaSbom.V*.addin') -ErrorAction SilentlyContinue)
if ($AddInFolder) { $packages += @(Get-ChildItem -Path (Join-Path $AddInFolder 'TiaSbom.V*.addin')) }
if ($packages.Count -eq 0) { throw 'No Add-In found. Run build.ps1 -Api Vnn -AddIn first.' }

$target = Join-Path $Output "TiaSbom-$version"
New-Item -ItemType Directory -Force (Join-Path $target 'addins') | Out-Null
foreach ($file in 'install.ps1', 'install.cmd', 'check.cmd', 'update.ps1', 'update.cmd', 'tiasbom.env.example') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $target -Force
}
# install.ps1 records it in %ProgramData%\TiaSbom\version.txt, update.cmd compares it with the newest release.
Set-Content -LiteralPath (Join-Path $target 'VERSION') -Value $version -Encoding ASCII
foreach ($package in $packages) {
    Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $target 'addins') -Force
    Write-Host ('{0}  {1:yyyy-MM-dd HH:mm}  {2}' -f $package.Name, $package.LastWriteTime, $package.FullName)
}

# tiasbom.exe for check.cmd. --check loads no Openness assembly, so a build of any API works on every PC.
$cli = Get-ChildItem -Path (Join-Path $root 'src\TiaSbom.Cli\bin\V*\Release\net48\tiasbom.exe') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($cli) {
    $tools = Join-Path $target 'tools'
    New-Item -ItemType Directory -Force $tools | Out-Null
    Get-ChildItem -LiteralPath $cli.DirectoryName -File |
        Where-Object { $_.Extension -in '.exe', '.dll', '.config' -and $_.Name -notlike 'Siemens.*' } |
        Copy-Item -Destination $tools -Force
    Write-Host "tools: $($cli.FullName)"
}
else {
    Write-Warning 'No tiasbom.exe built, the package has no check.cmd tool. Run build.ps1 first.'
}

$zip = "$target.zip"
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zip.sha256" -Value "SHA256: $hash" -Encoding ASCII
Write-Host "Package: $zip"
Write-Host 'tiasbom.env is not included on purpose. Keep the filled-in file on the protected deployment share.'
Write-Host ''
Write-Host "Release in Gitea: tag v$version, asset $(Split-Path -Leaf $zip), this line in the description:"
Write-Host "SHA256: $hash" -ForegroundColor Green
