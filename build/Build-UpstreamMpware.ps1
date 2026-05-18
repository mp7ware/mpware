[CmdletBinding()]
param(
    [string]$VendorRoot,
    [string]$OutputRoot,
    [switch]$InstallPs2Exe
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $VendorRoot) {
    $VendorRoot = Join-Path $root 'vendor\ZOICWARE'
}
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $root 'dist\mpware'
}

$workspaceRootResolved = (Resolve-Path -LiteralPath $root).Path
$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
$runtimeDir = Join-Path $outputRootFull '_FOLDERMUSTBEONCDRIVE'
$thirdPartyDir = Join-Path $outputRootFull 'third_party\ZOICWARE-attribution'
$launcherScript = Join-Path $outputRootFull 'mpware.launcher.ps1'
$launcherExe = Join-Path $outputRootFull 'mpware.exe'

function Assert-InWorkspace {
    param([Parameter(Mandatory)] [string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspaceRootResolved, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside workspace: $fullPath"
    }
}

function Replace-Range {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$StartMarker,
        [Parameter(Mandatory)] [string]$EndMarker,
        [Parameter(Mandatory)] [string]$Replacement,
        [int]$SearchStart = 0
    )

    $start = $Text.IndexOf($StartMarker, $SearchStart, [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "Start marker not found: $StartMarker"
    }

    $end = $Text.IndexOf($EndMarker, $start, [System.StringComparison]::Ordinal)
    if ($end -lt 0) {
        throw "End marker not found after $StartMarker`: $EndMarker"
    }

    return $Text.Substring(0, $start) + $Replacement + $Text.Substring($end)
}

function Rename-BrandText {
    param([Parameter(Mandatory)] [string]$Text)

    $renamed = $Text -replace '\bZOICWARE\b', 'mpware'
    $renamed = $renamed -replace '\bZoicware\b', 'mpware'
    $renamed = $renamed -replace '\bzoicware\b', 'mpware'

    # Keep attribution/help links pointing to the real upstream project.
    $renamed = $renamed.Replace('https://github.com/mpware/mpware', 'https://github.com/zoicware/ZOICWARE')
    $renamed = $renamed.Replace('github.com/mpware/mpware', 'github.com/zoicware/ZOICWARE')
    $renamed = $renamed.Replace('raw.githubusercontent.com/mpware/', 'raw.githubusercontent.com/zoicware/')
    $renamed = $renamed.Replace('api.github.com/repos/mpware/', 'api.github.com/repos/zoicware/')
    return $renamed
}

function Use-MpwareConfigNames {
    param([Parameter(Mandatory)] [string]$Text)

    $updated = $Text.Replace('zSettings.cfg', 'mpwareSettings.cfg')
    $updated = $updated.Replace('ZCONFIG.cfg', 'mpware-config.cfg')
    $updated = $updated.Replace('ZCONFIG$($date).cfg', 'mpware-config$($date).cfg')
    $updated = $updated.Replace('zLocation.tmp', 'mpwareLocation.tmp')
    $updated = $updated.Replace('RUN mpware.exe', 'mpware.exe')
    $updated = $updated.Replace('mpware.ps1.ps1', 'mpware.ps1')
    return $updated
}

function New-PlaceholderAssets {
    param([Parameter(Mandatory)] [string]$IconDir)

    Add-Type -AssemblyName System.Drawing

    if (-not (Test-Path -LiteralPath $IconDir)) {
        New-Item -ItemType Directory -Path $IconDir -Force | Out-Null
    }

    $scriptText = (Get-ChildItem -LiteralPath $runtimeDir -Recurse -File | Where-Object { $_.Extension -in '.ps1', '.psm1' } | ForEach-Object {
            Get-Content -LiteralPath $_.FullName -Raw
        }) -join "`n"

    $assetNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $knownAssets = @(
        'Powershell_black.ico',
        'RB_Empty.ico',
        'RB_Full.ico',
        'activation.png',
        'BIOS.png',
        'browser.png',
        'cleanBroom.png',
        'delete.png',
        'disableServices.png',
        'explorer.png',
        'gpuDriver.png',
        'greencheckIcon.png',
        'groupPolicy.png',
        'importExport.png',
        'networkDriver.png',
        'optionalTweaks.png',
        'packageInstall.png',
        'postInstall.png',
        'power.png',
        'questionIcon.png',
        'registry.png',
        'removeTasks.png',
        'repair.png',
        'restore.png',
        'scripts.png',
        'settingsIcon.png',
        'tweaks.png',
        'windows11.png',
        'zSearchIcon.png'
    )

    foreach ($knownAsset in $knownAssets) {
        [void]$assetNames.Add($knownAsset)
    }

    foreach ($match in [regex]::Matches($scriptText, '(?:\$Global:iconDir|\$iconDir)\\([^"''`r`n]+\.(?:png|ico))', 'IgnoreCase')) {
        [void]$assetNames.Add($match.Groups[1].Value)
    }

    foreach ($assetName in $assetNames) {
        $assetPath = Join-Path $IconDir $assetName
        $assetParent = Split-Path -Parent $assetPath
        if (-not (Test-Path -LiteralPath $assetParent)) {
            New-Item -ItemType Directory -Path $assetParent -Force | Out-Null
        }

        $bitmap = [System.Drawing.Bitmap]::new(64, 64)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(18, 18, 18))

        $rect = [System.Drawing.Rectangle]::new(8, 8, 48, 48)
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $rect,
            [System.Drawing.Color]::FromArgb(34, 197, 94),
            [System.Drawing.Color]::FromArgb(59, 130, 246),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
        )
        $graphics.FillEllipse($brush, $rect)

        $font = [System.Drawing.Font]::new('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
        $textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $letter = [System.IO.Path]::GetFileNameWithoutExtension($assetName).Substring(0, 1).ToLowerInvariant()
        $graphics.DrawString($letter, $font, $textBrush, [System.Drawing.RectangleF]::new(0, 0, 64, 64), $format)

        if ([System.IO.Path]::GetExtension($assetPath).Equals('.ico', [System.StringComparison]::OrdinalIgnoreCase)) {
            $handle = $bitmap.GetHicon()
            $icon = [System.Drawing.Icon]::FromHandle($handle)
            $stream = [System.IO.File]::Create($assetPath)
            try {
                $icon.Save($stream)
            }
            finally {
                $stream.Dispose()
                $icon.Dispose()
            }
        }
        else {
            $bitmap.Save($assetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }

        $graphics.Dispose()
        $brush.Dispose()
        $font.Dispose()
        $textBrush.Dispose()
        $format.Dispose()
        $bitmap.Dispose()
    }
}

function Test-PowerShellSyntax {
    param([Parameter(Mandatory)] [string[]]$Paths)

    foreach ($path in $Paths) {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) {
            $message = ($errors | ForEach-Object { "$($_.Extent.File):$($_.Extent.StartLineNumber): $($_.Message)" }) -join "`n"
            throw "PowerShell syntax validation failed:`n$message"
        }
    }
}

Assert-InWorkspace -Path $outputRootFull
Assert-InWorkspace -Path $VendorRoot

if (-not (Test-Path -LiteralPath (Join-Path $VendorRoot 'src\ZOICWARE.ps1'))) {
    $vendorRootFull = [System.IO.Path]::GetFullPath($VendorRoot)
    $vendorParent = Split-Path -Parent $vendorRootFull
    $zipPath = Join-Path $vendorParent 'ZOICWARE-main.zip'
    $extractRoot = Join-Path $vendorParent 'ZOICWARE-download'

    New-Item -ItemType Directory -Path $vendorParent -Force | Out-Null
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }

    Write-Host 'Downloading ZOICWARE upstream source...'
    Invoke-WebRequest -Uri 'https://github.com/zoicware/ZOICWARE/archive/refs/heads/main.zip' -UseBasicParsing -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force

    $extracted = Join-Path $extractRoot 'ZOICWARE-main'
    if (-not (Test-Path -LiteralPath $extracted)) {
        throw "Downloaded archive did not contain expected folder: $extracted"
    }

    if (Test-Path -LiteralPath $vendorRootFull) {
        Remove-Item -LiteralPath $vendorRootFull -Recurse -Force
    }
    Move-Item -LiteralPath $extracted -Destination $vendorRootFull -Force
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
}

$vendorRootResolved = (Resolve-Path -LiteralPath $VendorRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $vendorRootResolved 'src\ZOICWARE.ps1'))) {
    throw "Missing upstream source: $(Join-Path $vendorRootResolved 'src\ZOICWARE.ps1')"
}

if (Test-Path -LiteralPath $outputRootFull) {
    Remove-Item -LiteralPath $outputRootFull -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $thirdPartyDir) -Force | Out-Null

Copy-Item -Path (Join-Path $vendorRootResolved 'src\*') -Destination $runtimeDir -Recurse -Force
New-Item -ItemType Directory -Path $thirdPartyDir -Force | Out-Null
foreach ($docName in @('LICENSE', 'README.md', 'features.md', 'registrytweaks.md', 'UpdateNotes.md')) {
    $docPath = Join-Path $vendorRootResolved $docName
    if (Test-Path -LiteralPath $docPath) {
        Copy-Item -LiteralPath $docPath -Destination $thirdPartyDir -Force
    }
}

$mainScript = Join-Path $runtimeDir 'ZOICWARE.ps1'
$mpwareScript = Join-Path $runtimeDir 'mpware.ps1'
Rename-Item -LiteralPath $mainScript -NewName 'mpware.ps1' -Force

$main = Get-Content -LiteralPath $mpwareScript -Raw
$main = Replace-Range -Text $main -StartMarker '#check exclusion first' -EndMarker '#check if settings file is made' -Replacement @'
# mpware: automatic Microsoft Defender exclusions are intentionally disabled.
#check if settings file is made
'@
$main = Replace-Range -Text $main -StartMarker 'if (!$offlineMode -and !$dontCheck4Updates)' -EndMarker 'if ($null -eq $folder)' -Replacement @'
Write-Host 'mpware: upstream self-update check is disabled in this build.' -ForegroundColor DarkGray

'@
$main = $main.Replace("'WELCOME TO ZOICWARE'", "'WELCOME TO mpware'")
$main = $main.Replace("`$Global:folder = (Get-ChildItem -Path `$sysDrive -Filter '_FOLDERMUSTBEONCDRIVE' -Recurse -Directory -ErrorAction SilentlyContinue -Force | Where-Object Name -NotIn '`$Recycle.Bin' | Select-Object -First 1).FullName", "`$Global:folder = `$PSScriptRoot")
$main = $main.Replace('$Global:iconDir = "$folder\zoicwareIcons"', '$Global:iconDir = "$folder\mpwareIcons"')
$main = $main.Replace("`$startColor = [System.Drawing.Color]::FromArgb(61, 74, 102)", "`$startColor = [System.Drawing.Color]::FromArgb(18, 18, 18)")
$main = $main.Replace("`$endColor = [System.Drawing.Color]::FromArgb(0, 0, 0)", "`$endColor = [System.Drawing.Color]::FromArgb(0, 0, 0)")
$main = $main.Replace("-TooltipText 'Activates Windows with a product key using KMS.'", "-TooltipText 'Activation/KMS tooling is blocked in mpware.'")
$main = Rename-BrandText -Text $main
$main = Use-MpwareConfigNames -Text $main
Set-Content -LiteralPath $mpwareScript -Value $main -Encoding UTF8

$zFunctionsPath = Join-Path $runtimeDir 'zFunctions.psm1'
$z = Get-Content -LiteralPath $zFunctionsPath -Raw
$z = $z.Replace("`$settings['gpDefender'] = `$checkbox2", "# mpware: Defender-disable setting is blocked.`r`n  `$settings['gpDefender'] = `$checkbox2")
$gpStart = $z.IndexOf('function gpTweaks', [System.StringComparison]::Ordinal)
if ($gpStart -lt 0) { throw 'gpTweaks function not found.' }
$defenderBranchStart = $z.IndexOf('    if ($checkbox2.Checked) {', $gpStart, [System.StringComparison]::Ordinal)
$defenderBranchEnd = $z.IndexOf('    if ($checkbox3.Checked) {', $defenderBranchStart, [System.StringComparison]::Ordinal)
if ($defenderBranchStart -lt 0 -or $defenderBranchEnd -lt 0) {
    throw 'Unable to locate gpTweaks Defender branch.'
}
$defenderStub = @'
    if ($checkbox2.Checked) {
      Write-Status -Message 'mpware blocks the Disable Defender tweak. Microsoft Defender changes were not applied.' -Type Warning
      $checkbox2.Checked = $false
    }

    if ($checkbox3.Checked) {
'@
$z = $z.Substring(0, $defenderBranchStart) + $defenderStub + $z.Substring($defenderBranchEnd + '    if ($checkbox3.Checked) {'.Length)
$z = $z.Replace("`$checkbox2.Text = 'Disable Defender'", "`$checkbox2.Text = 'Disable Defender (blocked)'")
$z = $z.Replace("`$checkbox2.AutoSize = `$true`r`n    `$form.Controls.Add(`$checkbox2)", "`$checkbox2.AutoSize = `$true`r`n    `$checkbox2.Enabled = `$false`r`n    `$checkbox2.Checked = `$false`r`n    `$form.Controls.Add(`$checkbox2)")
$z = $z.Replace("`$checkbox2.Checked = `$gpDefender", "`$checkbox2.Checked = `$false")
$installKeyStart = $z.IndexOf('function install-key {', [System.StringComparison]::Ordinal)
$installKeyExport = $z.IndexOf('Export-ModuleMember -Function install-key', $installKeyStart, [System.StringComparison]::Ordinal)
if ($installKeyStart -lt 0 -or $installKeyExport -lt 0) {
    throw 'install-key function/export not found.'
}
$installKeyExportEnd = $installKeyExport + 'Export-ModuleMember -Function install-key'.Length
$installKeyReplacement = @'
function install-key {
  Write-Status -Message 'mpware blocks Windows activation/KMS tooling. Use a legitimate Windows license and activation flow.' -Type Warning
  Custom-MsgBox -message 'mpware does not include Windows activation or KMS tooling.' -type Warning | Out-Null
}
Export-ModuleMember -Function install-key
'@
$z = $z.Substring(0, $installKeyStart) + $installKeyReplacement + $z.Substring($installKeyExportEnd)
$z = $z.Replace("Add-MpPreference -ExclusionPath 'C:\Program Files\PBOTuner' -Force -ErrorAction SilentlyContinue", "Write-Status -Message 'mpware skipped the automatic Microsoft Defender exclusion for PBOTuner.' -Type Warning")
$z = $z.Replace('$form.Text = ''ZOICWARE''', '$form.Text = ''mpware''')
$z = $z.Replace("`$startColor = [System.Drawing.Color]::FromArgb(61, 74, 102)", "`$startColor = [System.Drawing.Color]::FromArgb(18, 18, 18)")
$z = Rename-BrandText -Text $z
$z = Use-MpwareConfigNames -Text $z
Set-Content -LiteralPath $zFunctionsPath -Value $z -Encoding UTF8

$configUiPath = Join-Path $runtimeDir 'configUI.ps1'
$configUi = Get-Content -LiteralPath $configUiPath -Raw
$configUi = Rename-BrandText -Text $configUi
$configUi = Use-MpwareConfigNames -Text $configUi
Set-Content -LiteralPath $configUiPath -Value $configUi -Encoding UTF8

$installerPath = Join-Path $runtimeDir 'Install-OtherScripts.ps1'
$installer = Get-Content -LiteralPath $installerPath -Raw
$installer = Rename-BrandText -Text $installer
$installer = $installer.Replace("`$checkbox3.Text = 'Strip Windows Defender'", "`$checkbox3.Text = 'Strip Windows Defender (blocked)'")
$installer = $installer.Replace("`$checkbox3.Checked = `$false`r`n`$Form.Controls.Add(`$checkbox3)", "`$checkbox3.Checked = `$false`r`n`$checkbox3.Enabled = `$false`r`n`$Form.Controls.Add(`$checkbox3)")
$installer = $installer.Replace('        if ($checkbox3.Checked) {', "        if (`$checkbox3.Checked) {`r`n            [System.Windows.Forms.MessageBox]::Show('mpware blocks Windows Defender stripping tools.', 'mpware', 'OK', 'Warning') | Out-Null`r`n            `$checkbox3.Checked = `$false`r`n        }`r`n`r`n        if (`$false) {")
$installer = Use-MpwareConfigNames -Text $installer
Set-Content -LiteralPath $installerPath -Value $installer -Encoding UTF8

Get-ChildItem -LiteralPath $runtimeDir -Recurse -File | Where-Object { $_.Extension -in '.ps1', '.psm1' } | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    $text = $text.Replace('ZOICWARE.ps1', 'mpware.ps1')
    $text = Use-MpwareConfigNames -Text $text
    Set-Content -LiteralPath $_.FullName -Value $text -Encoding UTF8
}

$currentVersion = [regex]::Match($main, "\`$currentVersion\s*=\s*'([^']+)'").Groups[1].Value
if ([string]::IsNullOrWhiteSpace($currentVersion)) {
    $currentVersion = 'mpware'
}
New-Item -ItemType File -Path (Join-Path $runtimeDir $currentVersion) -Force | Out-Null
New-PlaceholderAssets -IconDir (Join-Path $runtimeDir 'mpwareIcons')

$launcher = @'
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$script = Join-Path $root '_FOLDERMUSTBEONCDRIVE\mpware.ps1'

if (-not (Test-Path -LiteralPath $script)) {
    [System.Windows.Forms.MessageBox]::Show("Missing mpware runtime script:`n$script", 'mpware', 'OK', 'Error') | Out-Null
    exit 1
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"")

if ($isAdmin) {
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $script) -Wait
}
else {
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $script) -Verb RunAs -Wait
}
'@
Set-Content -LiteralPath $launcherScript -Value $launcher -Encoding UTF8

$notice = @'
# mpware upstream package

This package vendors the ZOICWARE source from https://github.com/zoicware/ZOICWARE under the MIT license and builds a lowercase `mpware.exe` launcher around a patched runtime copy.

Included:
- Patched ZOICWARE PowerShell source, modules, registry tweak files, context-menu `.reg` files, restore tooling, driver/install helper scripts, and documentation.
- `third_party/ZOICWARE-attribution`, upstream license and documentation files.

Changed in the mpware runtime:
- Public branding and config names changed to lowercase `mpware`.
- Automatic self-update to upstream ZOICWARE is disabled.
- Automatic Microsoft Defender exclusions are disabled.
- Windows activation/KMS tooling is blocked.
- The "Disable Defender" group-policy tweak and "Strip Windows Defender" helper are blocked.
- Placeholder black/modern icon assets are generated because the GitHub source archive does not include the release icon folder.
'@
Set-Content -LiteralPath (Join-Path $outputRootFull 'NOTICE.md') -Value $notice -Encoding UTF8

$packageReadme = @'
# mpware

Run `mpware.exe` from this folder. The executable is a launcher for the patched PowerShell runtime in `_FOLDERMUSTBEONCDRIVE`, so keep the folder structure together when moving or uploading it.

Recommended install/use:
1. Extract the zip.
2. Right-click `mpware.exe`.
3. Choose **Run as administrator**.

This build is based on ZOICWARE but patched and rebranded as lowercase `mpware`. See `NOTICE.md` for the changed and blocked paths.
'@
Set-Content -LiteralPath (Join-Path $outputRootFull 'README.md') -Value $packageReadme -Encoding UTF8

$syntaxPaths = Get-ChildItem -LiteralPath $runtimeDir -Recurse -File | Where-Object { $_.Extension -in '.ps1', '.psm1' } | Select-Object -ExpandProperty FullName
Test-PowerShellSyntax -Paths @($syntaxPaths + $launcherScript)

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
    Write-Warning 'PS2EXE is not installed, so the patched package and launcher .ps1 were created without mpware.exe.'
    Write-Host 'Install it with: Install-Module ps2exe -Scope CurrentUser'
    Write-Host "Then rerun: $PSCommandPath"
    return
}

$params = @{
    inputFile   = $launcherScript
    outputFile  = $launcherExe
    noConsole   = $true
    title       = 'mpware'
    description = 'mpware launcher for a patched ZOICWARE-based Windows 11 optimizer package'
    product     = 'mpware'
    company     = 'mpware'
    version     = '0.1.0'
}

& $command @params
Write-Host "Created mpware package: $outputRootFull"
Write-Host "Created executable: $launcherExe"
