function Write-Status {
  param(
    [string]$Message,
    [string]$Type = 'Output'
  )

  $color = 'Cyan'
  if ($Type -match 'warn') { $color = 'Yellow' }
  if ($Type -match 'err') { $color = 'Red' }
  if ($Type -match 'ok|done|success') { $color = 'Green' }
  Write-Host "[+] $Message" -ForegroundColor $color
}

function Check-Internet {
  try {
    return [System.Net.NetworkInformation.NetworkInterface]::GetIsNetworkAvailable()
  }
  catch {
    return $false
  }
}

function Disable-MpwareConsoleQuickEdit {
  try {
    if (-not ('MpwareNative.ConsoleMode' -as [type])) {
      Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace MpwareNative {
  public static class ConsoleMode {
      [DllImport("kernel32.dll", SetLastError = true)]
      public static extern IntPtr GetStdHandle(int nStdHandle);
      [DllImport("kernel32.dll", SetLastError = true)]
      public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);
      [DllImport("kernel32.dll", SetLastError = true)]
      public static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);
  }
}
"@
    }

    $stdinHandle = [MpwareNative.ConsoleMode]::GetStdHandle(-10)
    if ($stdinHandle -eq [IntPtr]::Zero -or $stdinHandle.ToInt64() -eq -1) {
      return
    }

    $mode = 0
    if ([MpwareNative.ConsoleMode]::GetConsoleMode($stdinHandle, [ref]$mode)) {
      $ENABLE_EXTENDED_FLAGS = 0x0080
      $ENABLE_QUICK_EDIT = 0x0040
      $ENABLE_INSERT_MODE = 0x0020
      $updatedMode = (($mode -bor $ENABLE_EXTENDED_FLAGS) -band (-bnot $ENABLE_QUICK_EDIT)) -band (-bnot $ENABLE_INSERT_MODE)
      [MpwareNative.ConsoleMode]::SetConsoleMode($stdinHandle, $updatedMode) | Out-Null
    }
  }
  catch {
  }
}

function Clear-MpwareFolderContents {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return
  }

  Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue |
    ForEach-Object {
      Remove-Item -LiteralPath $_.FullName -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function Create-ModernButton {
  param(
    [string]$Text,
    [System.Drawing.Point]$Location,
    [System.Drawing.Size]$Size,
    [scriptblock]$ClickAction,
    $DialogResult,
    [int]$borderSize = 1
  )

  $button = New-Object System.Windows.Forms.Button
  $button.Text = $Text
  if ($Location) { $button.Location = $Location }
  if ($Size) { $button.Size = $Size }
  if ($DialogResult) { $button.DialogResult = $DialogResult }
  $button.BackColor = [System.Drawing.Color]::FromArgb(0, 200, 210)
  $button.ForeColor = [System.Drawing.Color]::Black
  $button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
  $button.FlatAppearance.BorderSize = $borderSize
  $button.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
  if ($ClickAction) { $button.Add_Click($ClickAction) }
  return $button
}

function Test-MpwareWinget {
  return [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
}

function Test-MpwareElevated {
  try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  }
  catch {
    return $false
  }
}

function Open-MpwareUrl {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Url,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  Write-Status "Opening $Label download page..."
  Start-Process $Url | Out-Null
  Write-Status "$Label page opened in your browser." 'Success'
}

function Get-MpwareStateDirectory {
  $path = Join-Path $env:LOCALAPPDATA 'mpware'
  New-Item -ItemType Directory -Path $path -Force | Out-Null
  return $path
}

function Get-MpwareRegistrySnapshotPath {
  return Join-Path (Get-MpwareStateDirectory) 'registry-restore-snapshot.json'
}

function Get-MpwareRegistrySnapshotBackupDirectory {
  $path = Join-Path (Get-MpwareStateDirectory) 'registry-restore-backups'
  New-Item -ItemType Directory -Path $path -Force | Out-Null
  return $path
}

function Convert-MpwareRegPath {
  param([string]$RegPath)

  if ([string]::IsNullOrWhiteSpace($RegPath)) {
    return $null
  }

  $clean = $RegPath.Trim().Trim('[', ']')
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
        Hive         = $mapping.Hive
        SubKey       = $subKey
        ProviderPath = '{0}:\{1}' -f $mapping.Hive, $subKey
        RegPath      = '{0}\{1}' -f $mapping.Hive, $subKey
      }
    }
  }

  return $null
}

function Get-MpwareRegistryValueSnapshot {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RegPath,
    [string]$ValueName = ''
  )

  $converted = Convert-MpwareRegPath -RegPath $RegPath
  if (-not $converted) {
    return $null
  }

  $isDefault = [string]::IsNullOrWhiteSpace($ValueName)
  $normalizedName = if ($isDefault) { '' } else { $ValueName }

  if (-not (Test-Path -LiteralPath $converted.ProviderPath)) {
    return [pscustomobject]@{
      RegPath      = $converted.RegPath
      ProviderPath = $converted.ProviderPath
      ValueName    = $normalizedName
      IsDefault    = $isDefault
      Existed      = $false
      Kind         = ''
      Data         = $null
    }
  }

  $item = Get-Item -LiteralPath $converted.ProviderPath -ErrorAction SilentlyContinue
  if (-not $item) {
    return [pscustomobject]@{
      RegPath      = $converted.RegPath
      ProviderPath = $converted.ProviderPath
      ValueName    = $normalizedName
      IsDefault    = $isDefault
      Existed      = $false
      Kind         = ''
      Data         = $null
    }
  }

  $sentinel = New-Object object
  $lookupName = if ($isDefault) { '' } else { $normalizedName }
  $currentValue = $item.GetValue($lookupName, $sentinel, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
  if ([object]::ReferenceEquals($currentValue, $sentinel)) {
    return [pscustomobject]@{
      RegPath      = $converted.RegPath
      ProviderPath = $converted.ProviderPath
      ValueName    = $normalizedName
      IsDefault    = $isDefault
      Existed      = $false
      Kind         = ''
      Data         = $null
    }
  }

  $kind = $item.GetValueKind($lookupName).ToString()
  $data = switch ($kind) {
    'Binary' { [Convert]::ToBase64String([byte[]]$currentValue) }
    'None' { [Convert]::ToBase64String([byte[]]$currentValue) }
    'MultiString' { @($currentValue) }
    default { $currentValue }
  }

  return [pscustomobject]@{
    RegPath      = $converted.RegPath
    ProviderPath = $converted.ProviderPath
    ValueName    = $normalizedName
    IsDefault    = $isDefault
    Existed      = $true
    Kind         = $kind
    Data         = $data
  }
}

function Save-MpwareRegistrySnapshot {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ChecksPath,
    [bool]$IncludePowerPlan = $false,
    [bool]$IncludeStartPins = $false
  )

  if (-not (Test-Path -LiteralPath $ChecksPath)) {
    throw "Registry check file was not found: $ChecksPath"
  }

  $snapshotPath = Get-MpwareRegistrySnapshotPath
  $backupRoot = Get-MpwareRegistrySnapshotBackupDirectory
  Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

  $values = New-Object System.Collections.Generic.List[object]
  $deleteKeys = New-Object System.Collections.Generic.List[object]
  $seenValues = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
  $seenDeleteKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

  function Add-MpwareSnapshotValue {
    param([string]$RegPath, [string]$ValueName = '')

    $normalizedName = if ([string]::IsNullOrWhiteSpace($ValueName)) { '' } else { $ValueName }
    $id = '{0}|{1}' -f $RegPath, $normalizedName
    if (-not $seenValues.Add($id)) {
      return
    }

    $snapshot = Get-MpwareRegistryValueSnapshot -RegPath $RegPath -ValueName $normalizedName
    if ($snapshot) {
      $values.Add($snapshot)
    }
  }

  foreach ($row in @(Get-Content -LiteralPath $ChecksPath -ErrorAction Stop)) {
    $parts = $row -split "`t", 3
    if ($parts.Count -lt 2) {
      continue
    }

    $action = $parts[0]
    $regPath = $parts[1]
    $valueName = if ($parts.Count -ge 3) { $parts[2] } else { '' }

    if ([string]::IsNullOrWhiteSpace($regPath)) {
      continue
    }

    if ($action -eq 'deletekey') {
      if (-not $seenDeleteKeys.Add($regPath)) {
        continue
      }

      $converted = Convert-MpwareRegPath -RegPath $regPath
      if (-not $converted) {
        continue
      }

      $backupFile = ''
      $existed = Test-Path -LiteralPath $converted.ProviderPath
      if ($existed) {
        $safeName = 'key-' + ([guid]::NewGuid().ToString('N')) + '.reg'
        $backupFile = Join-Path $backupRoot $safeName
        $regExe = Join-Path $env:SystemRoot 'System32\reg.exe'
        $exportProcess = Start-Process -FilePath $regExe -ArgumentList @('export', $converted.RegPath, $backupFile, '/y') -Wait -PassThru -WindowStyle Hidden
        if ($exportProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $backupFile)) {
          throw "Failed to back up registry key before apply: $($converted.RegPath)"
        }
      }

      $deleteKeys.Add([pscustomobject]@{
          RegPath      = $converted.RegPath
          ProviderPath = $converted.ProviderPath
          Existed      = $existed
          BackupFile   = $backupFile
        })
      continue
    }

    Add-MpwareSnapshotValue -RegPath $regPath -ValueName $valueName
  }

  $startPinsState = [pscustomobject]@{
    Enabled             = $IncludeStartPins
    LayoutPath          = ''
    LayoutExisted       = $false
    LayoutContentBase64 = ''
  }

  if ($IncludeStartPins) {
    Add-MpwareSnapshotValue -RegPath 'HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer' -ValueName 'ConfigureStartPins'
    Add-MpwareSnapshotValue -RegPath 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' -ValueName 'ConfigureStartPins'
    Add-MpwareSnapshotValue -RegPath 'HKCU\SOFTWARE\Microsoft\PolicyManager\current\user\Start' -ValueName 'ConfigureStartPins'
    Add-MpwareSnapshotValue -RegPath 'HKCU\SOFTWARE\Microsoft\PolicyManager\current\user\Start' -ValueName 'ConfigureStartPins_ProviderSet'
    Add-MpwareSnapshotValue -RegPath 'HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start' -ValueName 'ConfigureStartPins'
    Add-MpwareSnapshotValue -RegPath 'HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start' -ValueName 'ConfigureStartPins_ProviderSet'

    $layoutPath = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Shell\LayoutModification.json'
    $startPinsState.LayoutPath = $layoutPath
    $startPinsState.LayoutExisted = Test-Path -LiteralPath $layoutPath
    if ($startPinsState.LayoutExisted) {
      $startPinsState.LayoutContentBase64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($layoutPath))
    }
  }

  $powerPlanState = [pscustomobject]@{
    Enabled    = $IncludePowerPlan
    ActiveGuid = ''
  }

  if ($IncludePowerPlan) {
    $active = powercfg /getactivescheme 2>$null
    foreach ($line in $active) {
      if ($line -match '([0-9a-fA-F-]{36})') {
        $powerPlanState.ActiveGuid = $matches[1]
        break
      }
    }
  }

  $snapshotValues = if ($values.Count -gt 0) { $values.ToArray() } else { @() }
  $snapshotDeletedKeys = if ($deleteKeys.Count -gt 0) { $deleteKeys.ToArray() } else { @() }

  $snapshot = [pscustomobject]@{
    Version        = 1
    CreatedAt      = (Get-Date).ToString('o')
    RegistryValues = $snapshotValues
    DeletedKeys    = $snapshotDeletedKeys
    Managed     = [pscustomobject]@{
      StartPins = $startPinsState
      PowerPlan = $powerPlanState
    }
  }

  $snapshot | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $snapshotPath -Encoding UTF8
  return $snapshotPath
}

function Convert-MpwareSnapshotData {
  param([pscustomobject]$Entry)

  switch ($Entry.Kind) {
    'Binary' { return [Convert]::FromBase64String([string]$Entry.Data) }
    'None' { return [Convert]::FromBase64String([string]$Entry.Data) }
    'DWord' { return [int]$Entry.Data }
    'QWord' { return [long]$Entry.Data }
    'MultiString' { return @($Entry.Data) }
    default { return $Entry.Data }
  }
}

function Get-MpwareRegistryBaseKey {
  param([string]$Hive)

  switch ($Hive.ToUpperInvariant()) {
    'HKLM' { return [Microsoft.Win32.Registry]::LocalMachine }
    'HKCU' { return [Microsoft.Win32.Registry]::CurrentUser }
    'HKCR' { return [Microsoft.Win32.Registry]::ClassesRoot }
    'HKU' { return [Microsoft.Win32.Registry]::Users }
    'HKCC' { return [Microsoft.Win32.Registry]::CurrentConfig }
    default { return $null }
  }
}

function Restore-MpwareRegistrySnapshot {
  param(
    [string]$SnapshotPath = $(Get-MpwareRegistrySnapshotPath)
  )

  if (-not (Test-Path -LiteralPath $SnapshotPath)) {
    return $false
  }

  $snapshot = Get-Content -LiteralPath $SnapshotPath -Raw -ErrorAction Stop | ConvertFrom-Json

  foreach ($keySnapshot in @($snapshot.DeletedKeys)) {
    if ([string]::IsNullOrWhiteSpace($keySnapshot.RegPath)) {
      continue
    }

    if ($keySnapshot.Existed -and -not [string]::IsNullOrWhiteSpace($keySnapshot.BackupFile) -and (Test-Path -LiteralPath $keySnapshot.BackupFile)) {
      if (Test-Path -LiteralPath $keySnapshot.ProviderPath) {
        Remove-Item -LiteralPath $keySnapshot.ProviderPath -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
      }

      $regExe = Join-Path $env:SystemRoot 'System32\reg.exe'
      $importProcess = Start-Process -FilePath $regExe -ArgumentList @('import', [string]$keySnapshot.BackupFile) -Wait -PassThru -WindowStyle Hidden
      if ($importProcess.ExitCode -ne 0) {
        throw "Failed to restore registry key backup: $($keySnapshot.RegPath)"
      }
    }
    elseif (-not $keySnapshot.Existed -and (Test-Path -LiteralPath $keySnapshot.ProviderPath)) {
      Remove-Item -LiteralPath $keySnapshot.ProviderPath -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    }
  }

  foreach ($entry in @($snapshot.RegistryValues)) {
    $converted = Convert-MpwareRegPath -RegPath $entry.RegPath
    if (-not $converted) {
      continue
    }

    if (-not $entry.Existed) {
      if (Test-Path -LiteralPath $converted.ProviderPath) {
        $key = (Get-MpwareRegistryBaseKey -Hive $converted.Hive).OpenSubKey($converted.SubKey, $true)
        if ($key) {
          $lookupName = if ($entry.IsDefault) { '' } else { [string]$entry.ValueName }
          try { $key.DeleteValue($lookupName, $false) } catch {}
          $key.Close()
        }
      }
      continue
    }

    $baseKey = Get-MpwareRegistryBaseKey -Hive $converted.Hive
    if (-not $baseKey) {
      continue
    }

    $key = $baseKey.CreateSubKey($converted.SubKey)
    if (-not $key) {
      throw "Failed to open registry key for restore: $($entry.RegPath)"
    }

    try {
      $lookupName = if ($entry.IsDefault) { '' } else { [string]$entry.ValueName }
      $data = Convert-MpwareSnapshotData -Entry $entry
      $kindName = if ([string]::IsNullOrWhiteSpace($entry.Kind)) { 'String' } else { [string]$entry.Kind }
      $kind = [Microsoft.Win32.RegistryValueKind]::$kindName
      $key.SetValue($lookupName, $data, $kind)
    }
    finally {
      $key.Close()
    }
  }

  if ($snapshot.Managed.StartPins.Enabled) {
    $layoutPath = [string]$snapshot.Managed.StartPins.LayoutPath
    if (-not [string]::IsNullOrWhiteSpace($layoutPath)) {
      if ($snapshot.Managed.StartPins.LayoutExisted) {
        New-Item -ItemType Directory -Path (Split-Path -Path $layoutPath -Parent) -Force | Out-Null
        [System.IO.File]::WriteAllBytes($layoutPath, [Convert]::FromBase64String([string]$snapshot.Managed.StartPins.LayoutContentBase64))
      }
      elseif (Test-Path -LiteralPath $layoutPath) {
        Remove-Item -LiteralPath $layoutPath -Force -ErrorAction SilentlyContinue
      }
    }
  }

  if ($snapshot.Managed.PowerPlan.Enabled -and -not [string]::IsNullOrWhiteSpace($snapshot.Managed.PowerPlan.ActiveGuid)) {
    powercfg /setactive ([string]$snapshot.Managed.PowerPlan.ActiveGuid) | Out-Null
  }

  return $true
}

function Invoke-MpwareWingetInstall {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Id,
    [Parameter(Mandatory = $true)]
    [string]$DisplayName,
    [string]$FallbackUrl
  )

  if (-not (Check-Internet)) {
    throw "Internet connection is required to install $DisplayName."
  }

  if (-not (Test-MpwareWinget)) {
    if ($FallbackUrl) {
      Write-Status "winget is not available. Opening the official $DisplayName download page instead." 'Warn'
      Open-MpwareUrl -Url $FallbackUrl -Label $DisplayName
      return
    }
    throw "winget is required to install $DisplayName on this PC."
  }

  Disable-MpwareConsoleQuickEdit
  Write-Status "Installing $DisplayName with winget..."
  $process = Start-Process -FilePath 'winget.exe' -ArgumentList @(
    'install',
    '--id', $Id,
    '-e',
    '--source', 'winget',
    '--accept-package-agreements',
    '--accept-source-agreements',
    '--disable-interactivity',
    '--silent'
  ) -NoNewWindow -Wait -PassThru
  if ($process.ExitCode -ne 0) {
    throw "winget install failed for $DisplayName (exit code $($process.ExitCode))."
  }
  Write-Status "$DisplayName install finished." 'Success'
}

function Install-MpwareBrowser {
  param(
    [ValidateSet('Brave', 'Firefox', 'Chrome')]
    [string]$Name
  )

  $targets = @{
    Brave = @{
      Id = 'Brave.Brave'
      Url = 'https://brave.com/download/'
    }
    Firefox = @{
      Id = 'Mozilla.Firefox'
      Url = 'https://www.mozilla.org/firefox/new/'
    }
    Chrome = @{
      Id = 'Google.Chrome.EXE'
      Url = 'https://www.google.com/chrome/'
    }
  }

  $target = $targets[$Name]
  Invoke-MpwareWingetInstall -Id $target.Id -DisplayName $Name -FallbackUrl $target.Url
}

function Install-MpwareProgram {
  param(
    [ValidateSet('NVIDIA App', 'Steam', 'Discord', 'Spotify')]
    [string]$Name
  )

  switch ($Name) {
    'NVIDIA App' {
      if (-not (Check-Internet)) {
        throw 'Internet connection is required to download NVIDIA App.'
      }
      Open-MpwareUrl -Url 'https://www.nvidia.com/en-us/software/nvidia-app/' -Label 'NVIDIA App'
      return
    }
    'Steam' {
      Invoke-MpwareWingetInstall -Id 'Valve.Steam' -DisplayName 'Steam' -FallbackUrl 'https://store.steampowered.com/about/'
      return
    }
    'Discord' {
      Invoke-MpwareWingetInstall -Id 'Discord.Discord' -DisplayName 'Discord' -FallbackUrl 'https://discord.com/download'
      return
    }
    'Spotify' {
      $spotifyUrl = 'https://www.spotify.com/download/windows/'

      if (Test-MpwareElevated) {
        Write-Status 'Spotify installs as a per-user app. Opening the official Spotify download page instead of running winget as administrator.' 'Warn'
        Open-MpwareUrl -Url $spotifyUrl -Label 'Spotify'
        return
      }

      try {
        Invoke-MpwareWingetInstall -Id 'Spotify.Spotify' -DisplayName 'Spotify' -FallbackUrl $spotifyUrl
      }
      catch {
        Write-Status "Spotify winget install failed. Opening the official download page instead. $($_.Exception.Message)" 'Warn'
        Open-MpwareUrl -Url $spotifyUrl -Label 'Spotify'
      }
      return
    }
  }
}

function Install-MpwarePackages {
  $packages = @(
    @{
      Id = 'Microsoft.VCRedist.2015+.x64'
      Name = 'Microsoft Visual C++ Redistributable (x64)'
      Url = 'https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170'
    }
    @{
      Id = 'Microsoft.VCRedist.2015+.x86'
      Name = 'Microsoft Visual C++ Redistributable (x86)'
      Url = 'https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170'
    }
    @{
      Id = 'Microsoft.DotNet.DesktopRuntime.8'
      Name = '.NET Desktop Runtime 8'
      Url = 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0'
    }
    @{
      Id = 'Microsoft.DirectX'
      Name = 'DirectX End-User Runtime'
      Url = 'https://www.microsoft.com/en-us/download/details.aspx?id=35'
    }
  )

  Write-Status 'Installing package bundle: VC++ redistributables, .NET Desktop Runtime 8, and DirectX...'
  foreach ($package in $packages) {
    Invoke-MpwareWingetInstall -Id $package.Id -DisplayName $package.Name -FallbackUrl $package.Url
  }
  Write-Status 'Package bundle finished.' 'Success'
}

function Remove-MpwareAppxPattern {
  param([string]$Pattern)

  $installed = @()
  try {
    $installed = Get-AppxPackage -AllUsers -ErrorAction Stop |
      Where-Object { $_.Name -like $Pattern -or $_.PackageFullName -like $Pattern }
  }
  catch {
    Write-Status "Falling back to current-user AppX lookup for $($Pattern): $($_.Exception.Message)" 'Warn'
    $installed = Get-AppxPackage -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like $Pattern -or $_.PackageFullName -like $Pattern }
  }

  foreach ($package in $installed) {
    Write-Status "Removing installed package $($package.Name)"
    try {
      Remove-AppxPackage -Package $package.PackageFullName -AllUsers -ErrorAction Stop
    }
    catch {
      try {
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
      }
      catch {
        Write-Status "Skipping installed package $($package.Name): $($_.Exception.Message)" 'Warn'
      }
    }
  }

  $provisioned = @()
  try {
    $provisioned = Get-AppxProvisionedPackage -Online -ErrorAction Stop |
      Where-Object { $_.DisplayName -like $Pattern -or $_.PackageName -like $Pattern }
  }
  catch {
    Write-Status "Skipping provisioned lookup for $($Pattern): $($_.Exception.Message)" 'Warn'
  }

  foreach ($package in $provisioned) {
    Write-Status "Removing provisioned package $($package.DisplayName)"
    try {
      Remove-AppxProvisionedPackage -Online -PackageName $package.PackageName -ErrorAction Stop | Out-Null
    }
    catch {
      Write-Status "Skipping provisioned package $($package.DisplayName): $($_.Exception.Message)" 'Warn'
    }
  }
}

function Disable-MpwareCopilot {
  Write-Status 'Disabling Copilot policy and taskbar entry'
  $keys = @(
    'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot',
    'HKLM:\Software\Policies\Microsoft\Windows\WindowsCopilot'
  )
  foreach ($key in $keys) {
    try {
      New-Item -Path $key -Force | Out-Null
      Set-ItemProperty -Path $key -Name 'TurnOffWindowsCopilot' -Type DWord -Value 1 -Force
    }
    catch {
      Write-Status "Skipping Copilot policy key $($key): $($_.Exception.Message)" 'Warn'
    }
  }

  $advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
  try {
    New-Item -Path $advanced -Force | Out-Null
    Set-ItemProperty -Path $advanced -Name 'ShowCopilotButton' -Type DWord -Value 0 -Force
  }
  catch {
    Write-Status "Skipping Copilot taskbar toggle: $($_.Exception.Message)" 'Warn'
  }

  $bingChat = 'HKLM:\Software\Microsoft\Windows\Shell\Copilot\BingChat'
  try {
    New-Item -Path $bingChat -Force | Out-Null
    Set-ItemProperty -Path $bingChat -Name 'IsUserEligible' -Type DWord -Value 0 -Force
  }
  catch {
    Write-Status "Skipping Copilot BingChat toggle: $($_.Exception.Message)" 'Warn'
  }
}

function Remove-MpwareOneDrive {
  Write-Status 'Removing OneDrive...'
  Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue

  $setupCandidates = @(
    "$env:SystemRoot\SysWOW64\OneDriveSetup.exe",
    "$env:SystemRoot\System32\OneDriveSetup.exe"
  )

  foreach ($setup in $setupCandidates) {
    if (Test-Path -LiteralPath $setup) {
      try {
        $process = Start-Process -FilePath $setup -ArgumentList '/uninstall' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -eq 0) {
          break
        }
      }
      catch {
        Write-Status "OneDrive uninstall helper failed from $($setup): $($_.Exception.Message)" 'Warn'
      }
    }
  }

  try {
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Force | Out-Null
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Name 'DisableFileSyncNGSC' -Type DWord -Value 1 -Force
  }
  catch {
    Write-Status "Skipping OneDrive policy toggle: $($_.Exception.Message)" 'Warn'
  }
}

function Invoke-MpwareDebloatPreset {
  Disable-MpwareConsoleQuickEdit
  $targets = @(
    '*Clipchamp.Clipchamp*',
    '*Microsoft.549981C3F5F10*',
    '*Microsoft.BingNews*',
    '*Microsoft.BingWeather*',
    '*Microsoft.BingSearch*',
    '*Microsoft.Copilot*',
    '*Microsoft.GetHelp*',
    '*Microsoft.Getstarted*',
    '*Microsoft.MicrosoftOfficeHub*',
    '*Microsoft.MicrosoftSolitaireCollection*',
    '*Microsoft.MixedReality.Portal*',
    '*Microsoft.Office.OneNote*',
    '*Microsoft.People*',
    '*Microsoft.PowerAutomateDesktop*',
    '*Microsoft.SkypeApp*',
    '*Microsoft.Todos*',
    '*Microsoft.Wallet*',
    '*Microsoft.Windows.DevHome*',
    '*Microsoft.Windows.Photos*',
    '*Microsoft.WindowsAlarms*',
    '*Microsoft.WindowsCamera*',
    '*Microsoft.WindowsFeedbackHub*',
    '*Microsoft.WindowsMaps*',
    '*Microsoft.WindowsSoundRecorder*',
    '*Microsoft.Windows.Copilot*',
    '*Microsoft.Windows.Ai.Copilot*',
    '*Microsoft.WindowsCommunicationsApps*',
    '*Microsoft.OutlookForWindows*',
    '*Microsoft.WindowsPhone*',
    '*MicrosoftWindows.Client.WebExperience*',
    '*Microsoft.YourPhone*',
    '*Microsoft.ZuneMusic*',
    '*Microsoft.ZuneVideo*',
    '*MicrosoftCorporationII.MicrosoftFamily*',
    '*MicrosoftCorporationII.QuickAssist*',
    '*MicrosoftTeams*',
    '*MSTeams*'
  )

  Write-Status 'Running recommended debloat preset...'
  Disable-MpwareCopilot
  Remove-MpwareOneDrive
  foreach ($pattern in ($targets | Sort-Object -Unique)) {
    Remove-MpwareAppxPattern -Pattern $pattern
  }
  Write-Status 'Debloat preset finished. Restart recommended.' 'Success'
}

function Show-MpwareCleanup {
  $ConfirmPreference = 'None'
  Disable-MpwareConsoleQuickEdit
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()

  $form = New-Object System.Windows.Forms.Form
  $form.Text = 'mpware cleanup'
  $form.Size = New-Object System.Drawing.Size(420, 430)
  $form.StartPosition = 'CenterScreen'
  $form.BackColor = [System.Drawing.Color]::Black
  $form.ForeColor = [System.Drawing.Color]::White
  $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

  $title = New-Object System.Windows.Forms.Label
  $title.Text = 'Cleanup options'
  $title.Location = New-Object System.Drawing.Point(18, 16)
  $title.Size = New-Object System.Drawing.Size(360, 24)
  $title.ForeColor = [System.Drawing.Color]::White
  $title.BackColor = [System.Drawing.Color]::Black
  $title.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
  $form.Controls.Add($title)

  $list = New-Object System.Windows.Forms.CheckedListBox
  $list.Location = New-Object System.Drawing.Point(18, 52)
  $list.Size = New-Object System.Drawing.Size(368, 260)
  $list.CheckOnClick = $true
  $list.BackColor = [System.Drawing.Color]::Black
  $list.ForeColor = [System.Drawing.Color]::White
  $list.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
  $items = @(
    'User temp files',
    'Local app temp files',
    'Windows temp files',
    'Prefetch',
    'Internet cache',
    'Recycle Bin',
    'Thumbnail cache',
    'DirectX shader cache',
    'NVIDIA shader cache',
    'Delivery Optimization cache',
    'Windows error reports'
  )
  foreach ($item in $items) { $list.Items.Add($item, $false) | Out-Null }
  $form.Controls.Add($list)

  $checkAll = Create-ModernButton -Text 'CHECK ALL' -Location (New-Object Drawing.Point(18, 332)) -Size (New-Object Drawing.Size(174, 34))
  $checkAll.Add_Click({
      for ($i = 0; $i -lt $list.Items.Count; $i++) {
        $list.SetItemChecked($i, $true)
      }
    })
  $form.Controls.Add($checkAll)

  $clean = Create-ModernButton -Text 'CLEAN' -Location (New-Object Drawing.Point(212, 332)) -Size (New-Object Drawing.Size(174, 34)) -DialogResult ([System.Windows.Forms.DialogResult]::OK)
  $form.Controls.Add($clean)
  $form.AcceptButton = $clean

  $selectedItems = @()
  try {
    $dialogResult = $form.ShowDialog()
    $selectedItems = @($list.CheckedItems | ForEach-Object { $_.ToString() })
  }
  finally {
    $form.Dispose()
  }

  if ($dialogResult -ne [System.Windows.Forms.DialogResult]::OK) {
    Write-Status 'Cleanup cancelled.' 'Warn'
    return
  }

  foreach ($item in $selectedItems) {
    try {
      Write-Status "Cleaning $item"
      switch ($item) {
        'User temp files' {
          Clear-MpwareFolderContents -Path $env:TEMP
        }
        'Local app temp files' {
          Clear-MpwareFolderContents -Path "$env:LocalAppData\Temp"
        }
        'Windows temp files' {
          Clear-MpwareFolderContents -Path "$env:SystemRoot\Temp"
        }
        'Prefetch' {
          Clear-MpwareFolderContents -Path "$env:SystemRoot\Prefetch"
        }
        'Internet cache' {
          Clear-MpwareFolderContents -Path "$env:LocalAppData\Microsoft\Windows\INetCache"
        }
        'Recycle Bin' {
          Clear-RecycleBin -Force -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
        }
        'Thumbnail cache' {
          Remove-Item -Path "$env:LocalAppData\Microsoft\Windows\Explorer\thumbcache_*.db" -Force -Confirm:$false -ErrorAction SilentlyContinue
        }
        'DirectX shader cache' {
          Clear-MpwareFolderContents -Path "$env:LocalAppData\D3DSCache"
        }
        'NVIDIA shader cache' {
          Clear-MpwareFolderContents -Path "$env:LocalAppData\NVIDIA\GLCache"
          Clear-MpwareFolderContents -Path "$env:USERPROFILE\AppData\LocalLow\NVIDIA\PerDriverVersion\DXCache"
        }
        'Delivery Optimization cache' {
          Clear-MpwareFolderContents -Path "$env:SystemRoot\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"
        }
        'Windows error reports' {
          Clear-MpwareFolderContents -Path "$env:ProgramData\Microsoft\Windows\WER"
        }
      }
    }
    catch {
      Write-Status "Skipping cleanup target $($item): $($_.Exception.Message)" 'Warn'
    }
  }
  Write-Status 'Cleanup finished.' 'Success'
}
