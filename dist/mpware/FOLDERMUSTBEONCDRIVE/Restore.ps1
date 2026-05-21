If (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]'Administrator')) {
  Start-Process PowerShell.exe -ArgumentList ("-NoProfile -File `"{0}`"" -f $PSCommandPath) -Verb RunAs
  Exit
}

$ConfirmPreference = 'None'
$ErrorActionPreference = 'Stop'
$runtimePath = Join-Path $PSScriptRoot 'MpwareRuntime.ps1'
if (Test-Path -LiteralPath $runtimePath) {
  . $runtimePath
}

if (-not (Get-Command Write-Status -ErrorAction SilentlyContinue)) {
  function Write-Status {
    param([string]$Message, [string]$Type = 'Output')
    $color = if ($Type -match 'err') { 'Red' } elseif ($Type -match 'warn') { 'Yellow' } elseif ($Type -match 'ok|done|success') { 'Green' } else { 'Cyan' }
    Write-Host "[+] $Message" -ForegroundColor $color
  }
}

if (Get-Command Disable-MpwareConsoleQuickEdit -ErrorAction SilentlyContinue) {
  Disable-MpwareConsoleQuickEdit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function New-MpwareButton {
  param([string]$Text, [int]$X, [int]$Y, [System.Windows.Forms.DialogResult]$DialogResult)

  $button = New-Object System.Windows.Forms.Button
  $button.Text = $Text
  $button.Location = New-Object System.Drawing.Point($X, $Y)
  $button.Size = New-Object System.Drawing.Size(95, 30)
  $button.DialogResult = $DialogResult
  $button.BackColor = [System.Drawing.Color]::FromArgb(0, 200, 210)
  $button.ForeColor = [System.Drawing.Color]::Black
  $button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
  $button.FlatAppearance.BorderSize = 1
  return $button
}

function Convert-MpwareRegistrySection {
  param([string]$Section)

  $clean = $Section.Trim()
  if ($clean.StartsWith('[') -and $clean.EndsWith(']')) {
    $clean = $clean.Substring(1, $clean.Length - 2)
  }
  if ($clean.StartsWith('-')) {
    $clean = $clean.Substring(1)
  }

  $mappings = @(
    @{ Prefix = 'HKEY_LOCAL_MACHINE\'; Hive = 'HKLM' },
    @{ Prefix = 'HKLM\'; Hive = 'HKLM' },
    @{ Prefix = 'HKEY_CURRENT_USER\'; Hive = 'HKCU' },
    @{ Prefix = 'HKCU\'; Hive = 'HKCU' },
    @{ Prefix = 'HKEY_CLASSES_ROOT\'; Hive = 'HKCR' },
    @{ Prefix = 'HKCR\'; Hive = 'HKCR' },
    @{ Prefix = 'HKEY_USERS\'; Hive = 'HKU' },
    @{ Prefix = 'HKU\'; Hive = 'HKU' },
    @{ Prefix = 'HKEY_CURRENT_CONFIG\'; Hive = 'HKCC' },
    @{ Prefix = 'HKCC\'; Hive = 'HKCC' }
  )

  foreach ($mapping in $mappings) {
    if ($clean.StartsWith($mapping.Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $subKey = $clean.Substring($mapping.Prefix.Length)
      return [pscustomobject]@{
        ProviderPath = '{0}:\{1}' -f $mapping.Hive, $subKey
        RegPath      = '{0}\{1}' -f $mapping.Hive, $subKey
      }
    }
  }

  return $null
}

function Get-MpwareRestoreEntries {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "Missing tweak file: $Path"
  }

  $entries = New-Object System.Collections.Generic.List[object]
  $touchedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
  $seenEntries = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
  $skippedDeleteOnly = 0
  $lines = Get-Content -LiteralPath $Path
  $currentSection = $null
  $deleteSection = $false

  for ($i = 0; $i -lt $lines.Count; $i++) {
    $raw = $lines[$i]
    $trimmed = $raw.Trim()

    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('Windows Registry Editor', [System.StringComparison]::OrdinalIgnoreCase) -or $trimmed.StartsWith(';')) {
      continue
    }

    if ($trimmed.StartsWith('[') -and $trimmed.EndsWith(']')) {
      $currentSection = $trimmed.Substring(1, $trimmed.Length - 2)
      $deleteSection = $currentSection.StartsWith('-')
      continue
    }

    if (-not $currentSection -or $trimmed.IndexOf('=') -lt 0) {
      continue
    }

    $valueLine = $raw.TrimEnd()
    while ($valueLine.TrimEnd().EndsWith('\') -and $i + 1 -lt $lines.Count) {
      $i++
      $valueLine += "`n" + $lines[$i].TrimEnd()
    }

    $converted = Convert-MpwareRegistrySection -Section $currentSection
    if (-not $converted) {
      continue
    }

    $null = $touchedPaths.Add($converted.ProviderPath)

    if ($deleteSection) {
      $skippedDeleteOnly++
      continue
    }

    $separator = $valueLine.IndexOf('=')
    $nameToken = $valueLine.Substring(0, $separator).Trim()
    $valueToken = $valueLine.Substring($separator + 1).Trim()

    if ($valueToken -eq '-') {
      $skippedDeleteOnly++
      continue
    }

    $entryKey = '{0}|{1}' -f $converted.ProviderPath, $nameToken
    if ($seenEntries.Add($entryKey)) {
      $entries.Add([pscustomobject]@{
          ProviderPath = $converted.ProviderPath
          RegPath      = $converted.RegPath
          ValueName    = if ($nameToken -eq '@') { $null } else { $nameToken.Trim('"') }
          IsDefault    = ($nameToken -eq '@')
        })
    }
  }

  [pscustomobject]@{
    Entries            = $entries
    TouchedPaths       = @($touchedPaths)
    SkippedDeleteOnly  = $skippedDeleteOnly
  }
}

function Get-MpwareManagedRestoreEntries {
  $managed = @(
    @{ ProviderPath = 'HKCU:\Control Panel\Colors'; RegPath = 'HKCU\Control Panel\Colors'; ValueName = 'Background'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Control Panel\Desktop'; RegPath = 'HKCU\Control Panel\Desktop'; ValueName = 'Wallpaper'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize'; RegPath = 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize'; ValueName = 'ColorPrevalence'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; RegPath = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; ValueName = 'AccentPalette'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; RegPath = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; ValueName = 'StartColorMenu'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; RegPath = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'; ValueName = 'AccentColorMenu'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\DWM'; RegPath = 'HKCU\Software\Microsoft\Windows\DWM'; ValueName = 'ColorPrevalence'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\DWM'; RegPath = 'HKCU\Software\Microsoft\Windows\DWM'; ValueName = 'AccentColor'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\DWM'; RegPath = 'HKCU\Software\Microsoft\Windows\DWM'; ValueName = 'ColorizationColor'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\Software\Microsoft\Windows\DWM'; RegPath = 'HKCU\Software\Microsoft\Windows\DWM'; ValueName = 'ColorizationAfterglow'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; RegPath = 'HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer'; ValueName = 'ConfigureStartPins'; IsDefault = $false },
    @{ ProviderPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; RegPath = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer'; ValueName = 'ConfigureStartPins'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\SOFTWARE\Microsoft\PolicyManager\current\user\Start'; RegPath = 'HKCU\SOFTWARE\Microsoft\PolicyManager\current\user\Start'; ValueName = 'ConfigureStartPins'; IsDefault = $false },
    @{ ProviderPath = 'HKCU:\SOFTWARE\Microsoft\PolicyManager\current\user\Start'; RegPath = 'HKCU\SOFTWARE\Microsoft\PolicyManager\current\user\Start'; ValueName = 'ConfigureStartPins_ProviderSet'; IsDefault = $false },
    @{ ProviderPath = 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Start'; RegPath = 'HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start'; ValueName = 'ConfigureStartPins'; IsDefault = $false },
    @{ ProviderPath = 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Start'; RegPath = 'HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start'; ValueName = 'ConfigureStartPins_ProviderSet'; IsDefault = $false }
  )

  return $managed | ForEach-Object { [pscustomobject]$_ }
}

function Remove-MpwareRegistryValue {
  param([pscustomobject]$Entry)

  if (-not (Test-Path -LiteralPath $Entry.ProviderPath)) {
    return 'missing'
  }

  if ($Entry.IsDefault) {
    & reg.exe delete $Entry.RegPath /ve /f *> $null
    if ($LASTEXITCODE -eq 0) {
      return 'removed'
    }
    return 'missing'
  }

  $properties = Get-ItemProperty -LiteralPath $Entry.ProviderPath -ErrorAction SilentlyContinue
  if (-not $properties -or -not ($properties.PSObject.Properties.Name -contains $Entry.ValueName)) {
    return 'missing'
  }

  Remove-ItemProperty -LiteralPath $Entry.ProviderPath -Name $Entry.ValueName -Force -ErrorAction Stop
  return 'removed'
}

function Remove-MpwareEmptyRegistryKeys {
  param([string[]]$ProviderPaths)

  foreach ($path in ($ProviderPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object Length -Descending -Unique)) {
    try {
      $currentPath = $path
      while (-not [string]::IsNullOrWhiteSpace($currentPath) -and $currentPath -match '^[A-Z]+:\\') {
        if (-not (Test-Path -LiteralPath $currentPath)) {
          break
        }

        $hasChildren = @(Get-ChildItem -LiteralPath $currentPath -ErrorAction SilentlyContinue).Count -gt 0
        if ($hasChildren) {
          break
        }

        $item = Get-Item -LiteralPath $currentPath -ErrorAction SilentlyContinue
        if (-not $item) {
          break
        }

        $valueNames = @($item.Property | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($valueNames.Count -gt 0) {
          break
        }

        Remove-Item -LiteralPath $currentPath -Force -Confirm:$false -ErrorAction Stop

        $parentPath = Split-Path -Path $currentPath -Parent
        if ([string]::IsNullOrWhiteSpace($parentPath) -or $parentPath -eq $currentPath -or $parentPath -match '^[A-Z]+:$') {
          break
        }

        $currentPath = $parentPath
      }
    }
    catch {
    }
  }
}

function Restart-MpwareShell {
  try {
    Stop-Process -Name StartMenuExperienceHost,ShellExperienceHost -Force -ErrorAction SilentlyContinue
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Process explorer.exe
  }
  catch {
  }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Restore Changes'
$form.Size = New-Object System.Drawing.Size(470, 190)
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.BackColor = [System.Drawing.Color]::Black
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$checkbox = New-Object System.Windows.Forms.CheckBox
$checkbox.Text = 'Remove registry values added by mpware tweaks'
$checkbox.ForeColor = [System.Drawing.Color]::White
$checkbox.BackColor = [System.Drawing.Color]::Black
$checkbox.Location = New-Object System.Drawing.Point(20, 28)
$checkbox.AutoSize = $true
$checkbox.Checked = $true
$form.Controls.Add($checkbox)

$note = New-Object System.Windows.Forms.Label
$note.Text = 'mpware restores the last Registry Tweaks snapshot when one is available. If not, it falls back to removing the current tweak values.'
$note.ForeColor = [System.Drawing.Color]::LightGray
$note.BackColor = [System.Drawing.Color]::Black
$note.Location = New-Object System.Drawing.Point(20, 58)
$note.Size = New-Object System.Drawing.Size(418, 38)
$form.Controls.Add($note)

$okButton = New-MpwareButton -Text 'Apply' -X 248 -Y 108 -DialogResult ([System.Windows.Forms.DialogResult]::OK)
$cancelButton = New-MpwareButton -Text 'Cancel' -X 353 -Y 108 -DialogResult ([System.Windows.Forms.DialogResult]::Cancel)
$form.Controls.Add($okButton)
$form.Controls.Add($cancelButton)
$form.AcceptButton = $okButton
$form.CancelButton = $cancelButton

$dialogResult = $form.ShowDialog()
$checked = $checkbox.Checked
$form.Dispose()

if ($dialogResult -ne [System.Windows.Forms.DialogResult]::OK -or -not $checked) {
  Write-Status 'Restore cancelled.' 'Warn'
  return
}

$usedSnapshot = $false
if (Get-Command Restore-MpwareRegistrySnapshot -ErrorAction SilentlyContinue) {
  try {
    Write-Status 'Checking for a saved restore snapshot...'
    $usedSnapshot = [bool](Restore-MpwareRegistrySnapshot)
    if ($usedSnapshot) {
      Write-Status 'Restored the last saved registry snapshot.' 'Success'
    }
    else {
      Write-Status 'No saved restore snapshot was found. Falling back to direct registry cleanup.' 'Warn'
    }
  }
  catch {
    Write-Status "Snapshot restore skipped: $($_.Exception.Message)" 'Warn'
  }
}

if ($usedSnapshot) {
  Restart-MpwareShell
  Write-Status 'Registry restore complete.' 'Success'
  return
}

$tweakFile = Join-Path $PSScriptRoot 'RegTweaks.txt'
$parsed = Get-MpwareRestoreEntries -Path $tweakFile
$managedEntries = Get-MpwareManagedRestoreEntries
$allEntries = @($parsed.Entries + $managedEntries)
$touchedPaths = @($parsed.TouchedPaths + ($managedEntries | ForEach-Object { $_.ProviderPath }))
$removed = 0
$missing = 0

Write-Status 'Removing registry values added by mpware tweaks...'
foreach ($entry in $allEntries) {
  try {
    $result = Remove-MpwareRegistryValue -Entry $entry
    if ($result -eq 'removed') {
      $removed++
    }
    else {
      $missing++
    }
  }
  catch {
    Write-Status "Skipping $($entry.RegPath): $($_.Exception.Message)" 'Warn'
  }
}

Remove-MpwareEmptyRegistryKeys -ProviderPaths $touchedPaths

$shellLayout = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Shell\LayoutModification.json'
if (Test-Path -LiteralPath $shellLayout) {
  Remove-Item -LiteralPath $shellLayout -Force -ErrorAction SilentlyContinue
}

Restart-MpwareShell

Write-Status "Removed $removed registry value(s)." 'Success'
if ($missing -gt 0) {
  Write-Status "$missing value(s) were already absent." 'Warn'
}
if ($parsed.SkippedDeleteOnly -gt 0) {
  Write-Status "$($parsed.SkippedDeleteOnly) delete-only tweak entries were skipped because their previous state cannot be reconstructed automatically." 'Warn'
}
Write-Status 'Registry restore complete.' 'Success'
