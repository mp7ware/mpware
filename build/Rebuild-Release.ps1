[CmdletBinding()]
param(
    [string]$PackageRoot
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageRoot) {
    $PackageRoot = Join-Path $root 'dist\mpware'
}

$launcherExe = Join-Path $PackageRoot 'mpware.exe'
$zipPath = Join-Path $root 'dist\mpware.zip'
$runtimeRoot = Join-Path $PackageRoot '_FOLDERMUSTBEONCDRIVE'
$launcherSource = Join-Path $PSScriptRoot 'MpwareLauncher.cs'
$launcherManifest = Join-Path $PSScriptRoot 'MpwareLauncher.manifest'
$launcherIcon = Join-Path $runtimeRoot 'mpwareIcons\Powershell_black.ico'

if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'mpware.ps1'))) {
    throw "Missing runtime script: $runtimeRoot"
}
if (-not (Test-Path -LiteralPath $launcherSource)) {
    throw "Missing launcher source: $launcherSource"
}

$csc = Get-Command csc.exe -ErrorAction SilentlyContinue
if (-not $csc) {
    $csc = Get-ChildItem 'C:\Windows\Microsoft.NET\Framework64','C:\Windows\Microsoft.NET\Framework' -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}
if (-not $csc) {
    throw 'Could not find csc.exe. Install .NET Framework developer tools or run on a standard Windows installation.'
}
$cscPath = if ($csc.PSObject.Properties.Name -contains 'Source') { $csc.Source } else { $csc.FullName }

$runtimeZip = Join-Path ([System.IO.Path]::GetTempPath()) "mpware-runtime-$([guid]::NewGuid().ToString('N')).zip"
try {
    Compress-Archive -Path (Join-Path $runtimeRoot '*') -DestinationPath $runtimeZip -Force

    $cscArgs = @(
        '/nologo',
        '/target:winexe',
        "/out:$launcherExe",
        "/resource:$runtimeZip,mpwareRuntimeZip",
        '/reference:System.Windows.Forms.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.IO.Compression.dll',
        '/reference:System.IO.Compression.FileSystem.dll'
    )

    if (Test-Path -LiteralPath $launcherManifest) {
        $cscArgs += "/win32manifest:$launcherManifest"
    }
    if (Test-Path -LiteralPath $launcherIcon) {
        $cscArgs += "/win32icon:$launcherIcon"
    }

    $cscArgs += $launcherSource
    & $cscPath @cscArgs
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $runtimeZip -Force -ErrorAction SilentlyContinue
}

Compress-Archive -Path (Join-Path $PackageRoot '*') -DestinationPath $zipPath -Force
Write-Host "Refreshed release package: $PackageRoot"
Write-Host "Refreshed exe: $launcherExe"
Write-Host "Refreshed zip: $zipPath"
