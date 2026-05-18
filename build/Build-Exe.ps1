[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$InstallPs2Exe,
    [switch]$RequireAdmin
)

Set-StrictMode -Version 2.0

$root = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$mainScript = Join-Path $srcDir 'mpware.ps1'
$tweakScript = Join-Path $srcDir 'mpware.tweaks.ps1'
$bundleScript = Join-Path $distDir 'mpware.bundle.ps1'

if (-not $OutputPath) {
    $OutputPath = Join-Path $distDir 'mpware.exe'
}

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $mainScript)) {
    throw "Missing main script: $mainScript"
}

if (-not (Test-Path -LiteralPath $tweakScript)) {
    throw "Missing tweak script: $tweakScript"
}

$main = Get-Content -LiteralPath $mainScript -Raw
$tweaks = Get-Content -LiteralPath $tweakScript -Raw
$includeLine = ". (Join-Path `$PSScriptRoot 'mpware.tweaks.ps1')"
$bundle = $main.Replace($includeLine, "# Inlined from src\mpware.tweaks.ps1`r`n$tweaks")
$bundle | Set-Content -LiteralPath $bundleScript -Encoding UTF8

Write-Host "Created bundled script: $bundleScript"

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
    Write-Warning 'PS2EXE is not installed, so only the bundled .ps1 was created.'
    Write-Host 'Install it with: Install-Module ps2exe -Scope CurrentUser'
    Write-Host 'Then rerun: .\build\Build-Exe.ps1'
    return
}

$params = @{
    inputFile   = $bundleScript
    outputFile  = $OutputPath
    noConsole   = $true
    title       = 'mpware'
    description = 'Windows 11 performance, privacy, and debloat tuner'
    product     = 'mpware'
    company     = 'Local'
    version     = '0.1.0'
}

if ($RequireAdmin) {
    $params['requireAdmin'] = $true
}

& $command @params
Write-Host "Created executable: $OutputPath"



