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

function Search-File {
  param([string]$filter)

  $roots = @($Global:nvidiaFolder, $Global:folder, $PSScriptRoot) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique

  foreach ($root in $roots) {
    if (Test-Path -LiteralPath $root) {
      $match = Get-ChildItem -LiteralPath $root -Recurse -File -Filter $filter -ErrorAction SilentlyContinue |
        Select-Object -First 1
      if ($match) {
        return $match.FullName
      }
    }
  }
  return $null
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

function Remove-MpwarePath {
  param([string]$Path)

  if (-not (Test-Path -Path $Path)) {
    return
  }

  Remove-Item -Path $Path -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
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

function Custom-MsgBox {
  param(
    [string]$message,
    [string]$type = 'None'
  )

  Add-Type -AssemblyName System.Windows.Forms
  $buttons = [System.Windows.Forms.MessageBoxButtons]::OK
  $icon = [System.Windows.Forms.MessageBoxIcon]::Information
  if ($type -eq 'Question') {
    $buttons = [System.Windows.Forms.MessageBoxButtons]::OKCancel
    $icon = [System.Windows.Forms.MessageBoxIcon]::Question
  }
  elseif ($type -match 'Warn') {
    $icon = [System.Windows.Forms.MessageBoxIcon]::Warning
  }
  elseif ($type -match 'Error') {
    $icon = [System.Windows.Forms.MessageBoxIcon]::Error
  }

  $result = [System.Windows.Forms.MessageBox]::Show($message, 'mpware', $buttons, $icon)
  if ($result -eq [System.Windows.Forms.DialogResult]::OK) { return 'OK' }
  return 'Cancel'
}

function Test-MpwareWinget {
  return [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
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

  Write-Status "Installing $DisplayName with winget..."
  & winget install --id $Id -e --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity --silent
  if ($LASTEXITCODE -ne 0) {
    throw "winget install failed for $DisplayName (exit code $LASTEXITCODE)."
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
    [ValidateSet('NVIDIA App', 'Steam', 'Discord')]
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

  $installed = Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like $Pattern -or $_.PackageFullName -like $Pattern }
  foreach ($package in $installed) {
    Write-Status "Removing installed package $($package.Name)"
    try {
      Remove-AppxPackage -Package $package.PackageFullName -AllUsers -ErrorAction Stop
    }
    catch {
      Remove-AppxPackage -Package $package.PackageFullName -ErrorAction SilentlyContinue
    }
  }

  $provisioned = Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like $Pattern -or $_.PackageName -like $Pattern }
  foreach ($package in $provisioned) {
    Write-Status "Removing provisioned package $($package.DisplayName)"
    Remove-AppxProvisionedPackage -Online -PackageName $package.PackageName -ErrorAction SilentlyContinue | Out-Null
  }
}

function Disable-MpwareCopilot {
  Write-Status 'Disabling Copilot policy and taskbar entry'
  $keys = @(
    'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot',
    'HKLM:\Software\Policies\Microsoft\Windows\WindowsCopilot'
  )
  foreach ($key in $keys) {
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name 'TurnOffWindowsCopilot' -Type DWord -Value 1 -Force
  }

  $advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
  New-Item -Path $advanced -Force | Out-Null
  Set-ItemProperty -Path $advanced -Name 'ShowCopilotButton' -Type DWord -Value 0 -Force

  $bingChat = 'HKLM:\Software\Microsoft\Windows\Shell\Copilot\BingChat'
  New-Item -Path $bingChat -Force | Out-Null
  Set-ItemProperty -Path $bingChat -Name 'IsUserEligible' -Type DWord -Value 0 -Force
}

function Invoke-MpwareDebloatPreset {
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
  foreach ($pattern in ($targets | Sort-Object -Unique)) {
    Remove-MpwareAppxPattern -Pattern $pattern
  }
  Write-Status 'Debloat preset finished. Restart recommended.' 'Success'
}

function Show-MpwareCleanup {
  $ConfirmPreference = 'None'
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
    'Windows temp files',
    'Recycle Bin',
    'Thumbnail cache',
    'DirectX shader cache',
    'NVIDIA shader cache',
    'Delivery Optimization cache',
    'Windows error reports',
    'Event Viewer logs'
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
    Write-Status "Cleaning $item"
    switch ($item) {
      'User temp files' {
        Clear-MpwareFolderContents -Path $env:TEMP
      }
      'Windows temp files' {
        Clear-MpwareFolderContents -Path "$env:SystemRoot\Temp"
      }
      'Recycle Bin' {
        Clear-RecycleBin -Force -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
      }
      'Thumbnail cache' {
        Remove-Item -Path "$env:LocalAppData\Microsoft\Windows\Explorer\thumbcache_*.db" -Force -Confirm:$false -ErrorAction SilentlyContinue
      }
      'DirectX shader cache' {
        Remove-MpwarePath -Path "$env:LocalAppData\D3DSCache\*"
      }
      'NVIDIA shader cache' {
        Remove-MpwarePath -Path "$env:LocalAppData\NVIDIA\GLCache"
        Remove-MpwarePath -Path "$env:USERPROFILE\AppData\LocalLow\NVIDIA\PerDriverVersion\DXCache"
      }
      'Delivery Optimization cache' {
        Remove-MpwarePath -Path "$env:SystemRoot\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache\*"
      }
      'Windows error reports' {
        Remove-MpwarePath -Path "$env:ProgramData\Microsoft\Windows\WER\*"
      }
      'Event Viewer logs' {
        wevtutil el | ForEach-Object { wevtutil cl "$_" 2>$null | Out-Null }
      }
    }
  }
  Write-Status 'Cleanup finished.' 'Success'
}
