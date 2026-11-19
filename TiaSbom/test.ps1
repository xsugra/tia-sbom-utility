<#
  Runs the automated tests on Windows (VM or PC with .NET SDK). No TIA Portal needed.

    .\test.ps1                 net48 and net8.0: unit tests, error messages, CycloneDX schema (net8.0)
    .\test.ps1 -Framework net48
    .\test.ps1 -Gitea          also the integration tests; needs TIASBOM_TEST_GITEA_URL and TIASBOM_TEST_TOKEN

  Integration tests create and delete repositories in TIASBOM_TEST_OWNER (default sbom-test-org).
  Never point them at the production Gitea. See docs/12-automaticke-testy.md.
#>
param([ValidateSet('all', 'net48', 'net8.0')][string]$Framework = 'all', [switch]$Gitea)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
if (-not $Gitea) { Remove-Item Env:TIASBOM_TEST_GITEA_URL, Env:TIASBOM_TEST_TOKEN -ErrorAction SilentlyContinue }
elseif (-not $env:TIASBOM_TEST_GITEA_URL -or -not $env:TIASBOM_TEST_TOKEN) { throw 'Set TIASBOM_TEST_GITEA_URL and TIASBOM_TEST_TOKEN first.' }

$frameworks = if ($Framework -eq 'all') { 'net48', 'net8.0' } else { @($Framework) }
$failed = $false
foreach ($f in $frameworks) {
    Write-Host "=== $f" -ForegroundColor Cyan
    dotnet test "$PSScriptRoot\tests\TiaSbom.Tests" -f $f --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { Write-Host 'FAILED' -ForegroundColor Red; exit 1 }
Write-Host 'OK' -ForegroundColor Green
