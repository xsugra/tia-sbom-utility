<#
  Collects the built Add-Ins and the installer into dist\TiaSbom-<version>, plus a ZIP of it.

    .\deploy\package.ps1

  Each Add-In can only be built on a PC with its TIA version. Run this on each build machine and copy the files
  from addins\ into one folder, or add a package built elsewhere with -AddInFolder.
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
foreach ($file in 'install.ps1', 'install.cmd', 'tiasbom.env.example') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $target -Force
}
foreach ($package in $packages) {
    Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $target 'addins') -Force
    Write-Host ('{0}  {1:yyyy-MM-dd HH:mm}  {2}' -f $package.Name, $package.LastWriteTime, $package.FullName)
}

$zip = "$target.zip"
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip -Force
Write-Host "Package: $zip"
Write-Host 'tiasbom.env is not included on purpose. Keep the filled-in file on the protected deployment share.'
