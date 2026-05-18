[CmdletBinding()]
param(
    [string]$PackageRoot,
    [switch]$InstallPs2Exe
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageRoot) {
    $PackageRoot = Join-Path $root 'dist\mpware'
}

$launcherScript = Join-Path $PackageRoot 'mpware.launcher.ps1'
$launcherExe = Join-Path $PackageRoot 'mpware.exe'
$zipPath = Join-Path $root 'dist\mpware.zip'

if (-not (Test-Path -LiteralPath $launcherScript)) {
    throw "Missing launcher script: $launcherScript"
}

$command = @(
    Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue
    Get-Command ps2exe -ErrorAction SilentlyContinue
) | Select-Object -First 1

if (-not $command -and $InstallPs2Exe) {
    Install-Module ps2exe -Scope CurrentUser -Force
    Import-Module ps2exe -Force
    $command = @(
        Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue
        Get-Command ps2exe -ErrorAction SilentlyContinue
    ) | Select-Object -First 1
}

if (-not $command) {
    Write-Warning 'PS2EXE is not installed, so only the release zip will be refreshed.'
}
else {
    $params = @{
        inputFile   = $launcherScript
        outputFile  = $launcherExe
        noConsole   = $true
        title       = 'mpware'
        description = 'mpware Windows 11 optimizer launcher'
        product     = 'mpware'
        company     = 'mpware'
        version     = '0.1.0'
    }
    & $command @params
}

Compress-Archive -Path (Join-Path $PackageRoot '*') -DestinationPath $zipPath -Force
Write-Host "Refreshed release package: $PackageRoot"
Write-Host "Refreshed zip: $zipPath"

