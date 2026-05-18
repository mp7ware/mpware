Set-StrictMode -Version 2.0

$script:ScriptPath = $PSCommandPath
$script:AppRoot = Split-Path -Parent $PSScriptRoot
$script:ProfileDir = Join-Path $script:AppRoot 'profiles'
$script:LogDir = Join-Path $script:AppRoot 'logs'

foreach ($dir in @($script:ProfileDir, $script:LogDir)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

. (Join-Path $PSScriptRoot 'mpware.tweaks.ps1')

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Xaml

$script:Tweaks = @(Get-mpwareTweaks)
$script:CheckBoxes = @{}
$script:StateLabels = @{}

function Test-mpwareAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-mpwareBrush {
    param([string]$Color)

    $converter = New-Object Windows.Media.BrushConverter
    return $converter.ConvertFromString($Color)
}

function New-mpwareThickness {
    param(
        [double]$Left,
        [double]$Top,
        [double]$Right,
        [double]$Bottom
    )

    return New-Object Windows.Thickness -ArgumentList $Left, $Top, $Right, $Bottom
}

function Write-mpwareLog {
    param([string]$Message)

    $timestamp = Get-Date -Format 'HH:mm:ss'
    $script:LogBox.AppendText("[$timestamp] $Message`r`n")
    $script:LogBox.ScrollToEnd()
    Flush-mpwareUi
}

function Flush-mpwareUi {
    try {
        $frame = New-Object Windows.Threading.DispatcherFrame
        $callback = [Windows.Threading.DispatcherOperationCallback]{
            param([object]$Frame)
            $Frame.Continue = $false
            return $null
        }
        [Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke(
            [Windows.Threading.DispatcherPriority]::Background,
            $callback,
            $frame
        ) | Out-Null
        [Windows.Threading.Dispatcher]::PushFrame($frame)
    }
    catch {
    }
}

function Get-SelectedmpwareTweaks {
    return @($script:Tweaks | Where-Object {
        $script:CheckBoxes.ContainsKey($_.Id) -and $script:CheckBoxes[$_.Id].IsChecked -eq $true
    })
}

function Set-mpwareBusyState {
    param([bool]$Busy)

    $script:ApplyButton.IsEnabled = -not $Busy
    $script:UndoButton.IsEnabled = -not $Busy
    $script:LoadProfileButton.IsEnabled = -not $Busy
    $script:SaveProfileButton.IsEnabled = -not $Busy
    $script:SelectRecommendedButton.IsEnabled = -not $Busy
    $script:ClearSelectionButton.IsEnabled = -not $Busy
    $script:RefreshStateButton.IsEnabled = -not $Busy

    if ($Busy) {
        $script:BusyBar.Visibility = 'Visible'
        $script:BusyBar.IsIndeterminate = $true
    }
    else {
        $script:BusyBar.IsIndeterminate = $false
        $script:BusyBar.Visibility = 'Collapsed'
    }
}

function Update-mpwareAdminStatus {
    $isAdmin = Test-mpwareAdministrator
    if ($isAdmin) {
        $script:AdminStatus.Text = 'Administrator'
        $script:AdminStatus.Foreground = Get-mpwareBrush '#4ADE80'
    }
    else {
        $script:AdminStatus.Text = 'Standard user'
        $script:AdminStatus.Foreground = Get-mpwareBrush '#FBBF24'
    }
}

function Update-mpwareSelectedCount {
    $count = (Get-SelectedmpwareTweaks).Count
    $script:TweakCountText.Text = "$count selected / $($script:Tweaks.Count) tweaks"
}

function Update-mpwareStates {
    Write-mpwareLog 'Refreshing tweak states...'

    foreach ($tweak in $script:Tweaks) {
        if (-not $script:StateLabels.ContainsKey($tweak.Id)) {
            continue
        }

        $state = 'Unknown'
        try {
            $state = & $tweak.GetState
        }
        catch {
            $state = 'Unknown'
        }

        $label = $script:StateLabels[$tweak.Id]
        $label.Text = "State: $state"

        switch ($state) {
            'Applied' { $label.Foreground = Get-mpwareBrush '#4ADE80' }
            'Partial' { $label.Foreground = Get-mpwareBrush '#FBBF24' }
            default { $label.Foreground = Get-mpwareBrush '#94A3B8' }
        }
    }

    Write-mpwareLog 'State refresh complete.'
}

function Request-mpwareAdminRelaunch {
    param([string]$Reason)

    $answer = [Windows.MessageBox]::Show(
        "$Reason`n`nRelaunch mpware as administrator?",
        'Administrator required',
        [Windows.MessageBoxButton]::YesNo,
        [Windows.MessageBoxImage]::Warning
    )

    if ($answer -eq [Windows.MessageBoxResult]::Yes) {
        if ([IO.Path]::GetExtension($script:ScriptPath) -eq '.exe') {
            Start-Process -FilePath $script:ScriptPath -Verb RunAs
        }
        else {
            $powerShellExe = (Get-Process -Id $PID).Path
            if (-not (Test-Path -LiteralPath $powerShellExe)) {
                $powerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            }
            Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$script:ScriptPath`"" -Verb RunAs
        }

        $script:Window.Close()
        return $true
    }

    return $false
}

function New-mpwareRestorePoint {
    if (-not (Test-mpwareAdministrator)) {
        Write-mpwareLog 'Restore point skipped because the app is not elevated.'
        return
    }

    try {
        Write-mpwareLog 'Creating system restore point...'
        Checkpoint-Computer -Description "mpware $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -RestorePointType MODIFY_SETTINGS
        Write-mpwareLog 'System restore point created.'
    }
    catch {
        Write-mpwareLog "Could not create restore point: $($_.Exception.Message)"
    }
}

function Invoke-mpwareSelection {
    param([ValidateSet('Apply', 'Undo')] [string]$Mode)

    $selected = Get-SelectedmpwareTweaks
    if ($selected.Count -eq 0) {
        [Windows.MessageBox]::Show('Select at least one tweak first.', 'Nothing selected', [Windows.MessageBoxButton]::OK, [Windows.MessageBoxImage]::Information) | Out-Null
        return
    }

    $dryRun = [bool]$script:PreviewOnlyCheckBox.IsChecked
    $requiresAdmin = @($selected | Where-Object { $_.RequiresAdmin }).Count -gt 0
    if ($requiresAdmin -and -not $dryRun -and -not (Test-mpwareAdministrator)) {
        Request-mpwareAdminRelaunch 'One or more selected tweaks need administrator rights.' | Out-Null
        return
    }

    $label = if ($Mode -eq 'Apply') { 'Applying' } else { 'Restoring' }
    Set-mpwareBusyState -Busy $true

    try {
        Write-mpwareLog "$label $($selected.Count) tweak(s). Preview only: $dryRun"

        if ($Mode -eq 'Apply' -and -not $dryRun -and [bool]$script:CreateRestorePointCheckBox.IsChecked) {
            New-mpwareRestorePoint
        }

        foreach ($tweak in $selected) {
            Write-mpwareLog "${label}: $($tweak.Name)"
            try {
                if ($Mode -eq 'Apply') {
                    & $tweak.Apply $dryRun ${function:Write-mpwareLog}
                }
                else {
                    & $tweak.Undo $dryRun ${function:Write-mpwareLog}
                }
            }
            catch {
                Write-mpwareLog "Error in $($tweak.Name): $($_.Exception.Message)"
            }
        }

        Write-mpwareLog "$Mode finished."
    }
    finally {
        Set-mpwareBusyState -Busy $false
        Update-mpwareStates
    }
}

function Save-mpwareProfile {
    $selectedIds = @((Get-SelectedmpwareTweaks) | ForEach-Object { $_.Id })
    $dialog = New-Object Microsoft.Win32.SaveFileDialog
    $dialog.Filter = 'mpware profile (*.json)|*.json|All files (*.*)|*.*'
    $dialog.InitialDirectory = $script:ProfileDir
    $dialog.FileName = 'my-mpware-profile.json'

    if ($dialog.ShowDialog() -eq $true) {
        $profile = [ordered]@{
            version          = 1
            app              = 'mpware'
            createdAt        = (Get-Date).ToString('o')
            selectedTweakIds = $selectedIds
        }

        $profile | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $dialog.FileName -Encoding UTF8
        Write-mpwareLog "Saved profile: $($dialog.FileName)"
    }
}

function Load-mpwareProfile {
    $dialog = New-Object Microsoft.Win32.OpenFileDialog
    $dialog.Filter = 'mpware profile (*.json)|*.json|All files (*.*)|*.*'
    $dialog.InitialDirectory = $script:ProfileDir

    if ($dialog.ShowDialog() -eq $true) {
        try {
            $profile = Get-Content -LiteralPath $dialog.FileName -Raw | ConvertFrom-Json
            $ids = @($profile.selectedTweakIds)
            foreach ($id in $script:CheckBoxes.Keys) {
                $script:CheckBoxes[$id].IsChecked = $ids -contains $id
            }

            Update-mpwareSelectedCount
            Write-mpwareLog "Loaded profile: $($dialog.FileName)"
        }
        catch {
            [Windows.MessageBox]::Show("Could not load profile: $($_.Exception.Message)", 'Profile error', [Windows.MessageBoxButton]::OK, [Windows.MessageBoxImage]::Error) | Out-Null
        }
    }
}

function Export-mpwareLog {
    $fileName = Join-Path $script:LogDir ("mpware-log-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $script:LogBox.Text | Set-Content -LiteralPath $fileName -Encoding UTF8
    Write-mpwareLog "Exported log: $fileName"
}

function Select-mpwareRecommended {
    foreach ($tweak in $script:Tweaks) {
        $script:CheckBoxes[$tweak.Id].IsChecked = [bool]$tweak.Recommended
    }

    Update-mpwareSelectedCount
    Write-mpwareLog 'Selected recommended tweaks.'
}

function Clear-mpwareSelection {
    foreach ($id in $script:CheckBoxes.Keys) {
        $script:CheckBoxes[$id].IsChecked = $false
    }

    Update-mpwareSelectedCount
    Write-mpwareLog 'Cleared selection.'
}

function New-mpwareTweakCard {
    param([pscustomobject]$Tweak)

    $border = New-Object Windows.Controls.Border
    $border.Background = Get-mpwareBrush '#101010'
    $border.BorderBrush = Get-mpwareBrush '#27272A'
    $border.BorderThickness = New-mpwareThickness 1 1 1 1
    $border.CornerRadius = New-Object Windows.CornerRadius -ArgumentList 8
    $border.Padding = New-mpwareThickness 14 12 14 12
    $border.Margin = New-mpwareThickness 0 0 0 10

    $tooltip = @"
$($Tweak.Description)

Apply: $($Tweak.ApplySummary)
Undo: $($Tweak.UndoSummary)
Risk: $($Tweak.Risk)
Requires admin: $($Tweak.RequiresAdmin)
"@
    $border.ToolTip = $tooltip.Trim()

    $dock = New-Object Windows.Controls.DockPanel
    $dock.LastChildFill = $true

    $checkBox = New-Object Windows.Controls.CheckBox
    $checkBox.VerticalAlignment = 'Top'
    $checkBox.Margin = New-mpwareThickness 0 3 12 0
    $checkBox.ToolTip = $tooltip.Trim()
    $checkBox.Add_Checked({ Update-mpwareSelectedCount })
    $checkBox.Add_Unchecked({ Update-mpwareSelectedCount })
    [Windows.Controls.DockPanel]::SetDock($checkBox, [Windows.Controls.Dock]::Left)
    [void]$dock.Children.Add($checkBox)
    $script:CheckBoxes[$Tweak.Id] = $checkBox

    $stack = New-Object Windows.Controls.StackPanel
    $stack.Orientation = 'Vertical'

    $topLine = New-Object Windows.Controls.StackPanel
    $topLine.Orientation = 'Horizontal'

    $name = New-Object Windows.Controls.TextBlock
    $name.Text = $Tweak.Name
    $name.FontSize = 14
    $name.FontWeight = 'SemiBold'
    $name.Foreground = Get-mpwareBrush '#F8FAFC'
    $name.VerticalAlignment = 'Center'
    [void]$topLine.Children.Add($name)

    $riskColors = @{
        Low    = '#052E16'
        Medium = '#422006'
        High   = '#450A0A'
    }
    $riskTextColors = @{
        Low    = '#86EFAC'
        Medium = '#FCD34D'
        High   = '#FCA5A5'
    }

    $pill = New-Object Windows.Controls.Border
    $pill.Background = Get-mpwareBrush $riskColors[$Tweak.Risk]
    $pill.CornerRadius = New-Object Windows.CornerRadius -ArgumentList 999
    $pill.Padding = New-mpwareThickness 8 2 8 3
    $pill.Margin = New-mpwareThickness 10 0 0 0
    $pill.VerticalAlignment = 'Center'
    $pillText = New-Object Windows.Controls.TextBlock
    $pillText.Text = $Tweak.Risk
    $pillText.FontSize = 11
    $pillText.FontWeight = 'SemiBold'
    $pillText.Foreground = Get-mpwareBrush $riskTextColors[$Tweak.Risk]
    $pill.Child = $pillText
    [void]$topLine.Children.Add($pill)

    if ($Tweak.RequiresAdmin) {
        $adminText = New-Object Windows.Controls.TextBlock
        $adminText.Text = 'Admin'
        $adminText.FontSize = 11
        $adminText.FontWeight = 'SemiBold'
        $adminText.Foreground = Get-mpwareBrush '#93C5FD'
        $adminText.Margin = New-mpwareThickness 10 2 0 0
        [void]$topLine.Children.Add($adminText)
    }

    [void]$stack.Children.Add($topLine)

    $description = New-Object Windows.Controls.TextBlock
    $description.Text = $Tweak.Description
    $description.FontSize = 12
    $description.Foreground = Get-mpwareBrush '#CBD5E1'
    $description.TextWrapping = 'Wrap'
    $description.Margin = New-mpwareThickness 0 6 0 0
    [void]$stack.Children.Add($description)

    $state = New-Object Windows.Controls.TextBlock
    $state.Text = 'State: Unknown'
    $state.FontSize = 11
    $state.Foreground = Get-mpwareBrush '#94A3B8'
    $state.Margin = New-mpwareThickness 0 8 0 0
    [void]$stack.Children.Add($state)
    $script:StateLabels[$Tweak.Id] = $state

    [void]$dock.Children.Add($stack)
    $border.Child = $dock

    return $border
}

function Build-mpwareTabs {
    $orderedCategories = @(
        'Performance',
        'Group Policy',
        'Scheduled Tasks',
        'Services',
        'Debloat',
        'Privacy',
        'Optional',
        'Context Menu',
        'Power Plans',
        'Windows 11',
        'Install',
        'Restore',
        'Cleanup',
        'Utilities',
        'Blocked'
    )
    $remaining = @($script:Tweaks.Category | Sort-Object -Unique | Where-Object { $orderedCategories -notcontains $_ })
    $categories = @($orderedCategories + $remaining | Where-Object { $_ })

    foreach ($category in $categories) {
        $categoryTweaks = @($script:Tweaks | Where-Object { $_.Category -eq $category })
        if ($categoryTweaks.Count -eq 0) {
            continue
        }

        $tab = New-Object Windows.Controls.TabItem
        $tab.Header = "$category ($($categoryTweaks.Count))"

        $scroll = New-Object Windows.Controls.ScrollViewer
        $scroll.VerticalScrollBarVisibility = 'Auto'
        $scroll.HorizontalScrollBarVisibility = 'Disabled'
        $scroll.Padding = New-mpwareThickness 2 14 10 4

        $panel = New-Object Windows.Controls.StackPanel
        $panel.Orientation = 'Vertical'

        foreach ($tweak in $categoryTweaks) {
            [void]$panel.Children.Add((New-mpwareTweakCard -Tweak $tweak))
        }

        $scroll.Content = $panel
        $tab.Content = $scroll
        [void]$script:TweakTabs.Items.Add($tab)
    }
}

$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="mpware"
        Width="1120"
        Height="760"
        MinWidth="980"
        MinHeight="650"
        WindowStartupLocation="CenterScreen"
        Background="#050505"
        FontFamily="Segoe UI"
        TextOptions.TextFormattingMode="Display">
    <Window.Resources>
        <Style TargetType="{x:Type Button}">
            <Setter Property="MinHeight" Value="34"/>
            <Setter Property="Padding" Value="14,6"/>
            <Setter Property="Margin" Value="0,0,8,8"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="{x:Type Button}">
                        <Border x:Name="Chrome"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="7"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Opacity" Value="0.88"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter TargetName="Chrome" Property="Opacity" Value="0.45"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key="PrimaryButton" TargetType="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="#E5E7EB"/>
            <Setter Property="Foreground" Value="#050505"/>
            <Setter Property="BorderBrush" Value="#F8FAFC"/>
        </Style>
        <Style x:Key="SecondaryButton" TargetType="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="#111111"/>
            <Setter Property="Foreground" Value="#E5E7EB"/>
            <Setter Property="BorderBrush" Value="#2F2F33"/>
        </Style>
        <Style x:Key="DangerButton" TargetType="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="#1F1012"/>
            <Setter Property="Foreground" Value="#FDA4AF"/>
            <Setter Property="BorderBrush" Value="#7F1D1D"/>
        </Style>
        <Style TargetType="{x:Type CheckBox}">
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Foreground" Value="#D4D4D8"/>
        </Style>
        <Style TargetType="{x:Type TabControl}">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style TargetType="{x:Type TabItem}">
            <Setter Property="Padding" Value="14,8"/>
            <Setter Property="Margin" Value="0,0,4,0"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="#D4D4D8"/>
            <Setter Property="Background" Value="#111111"/>
            <Setter Property="BorderBrush" Value="#2F2F33"/>
        </Style>
    </Window.Resources>

    <Grid Margin="22">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="190"/>
        </Grid.RowDefinitions>

        <Grid Grid.Row="0" Margin="0,0,0,18">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="mpware" FontSize="28" FontWeight="SemiBold" Foreground="#F8FAFC"/>
                <TextBlock Text="Windows 11 performance, privacy, and debloat tweaks with profiles, restore data, and live logs."
                           FontSize="13"
                           Foreground="#A1A1AA"
                           Margin="0,4,0,0"/>
            </StackPanel>
            <Border Grid.Column="1"
                    Background="#101010"
                    BorderBrush="#27272A"
                    BorderThickness="1"
                    CornerRadius="8"
                    Padding="12,8"
                    VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="Mode:" Foreground="#A1A1AA" FontSize="12" Margin="0,0,6,0"/>
                    <TextBlock x:Name="AdminStatus" Text="Checking" Foreground="#FBBF24" FontSize="12" FontWeight="SemiBold"/>
                </StackPanel>
            </Border>
        </Grid>

        <Border Grid.Row="1"
                Background="#0B0B0B"
                BorderBrush="#27272A"
                BorderThickness="1"
                CornerRadius="8"
                Padding="12"
                Margin="0,0,0,14">
            <DockPanel LastChildFill="True">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Top">
                    <CheckBox x:Name="PreviewOnlyCheckBox"
                              Content="Preview only"
                              IsChecked="True"
                              Margin="8,8,16,0"
                              ToolTip="When checked, mpware logs what would happen without changing Windows."/>
                    <CheckBox x:Name="CreateRestorePointCheckBox"
                              Content="Restore point"
                              IsChecked="True"
                              Margin="0,8,0,0"
                              ToolTip="When applying real changes as administrator, create a Windows restore point first."/>
                </StackPanel>

                <WrapPanel>
                    <Button x:Name="LoadProfileButton" Style="{StaticResource SecondaryButton}" Content="Load Profile"/>
                    <Button x:Name="SaveProfileButton" Style="{StaticResource SecondaryButton}" Content="Save Profile"/>
                    <Button x:Name="SelectRecommendedButton" Style="{StaticResource SecondaryButton}" Content="Recommended"/>
                    <Button x:Name="ClearSelectionButton" Style="{StaticResource SecondaryButton}" Content="Clear"/>
                    <Button x:Name="RefreshStateButton" Style="{StaticResource SecondaryButton}" Content="Refresh State"/>
                    <Button x:Name="ApplyButton" Style="{StaticResource PrimaryButton}" Content="Apply Selected"/>
                    <Button x:Name="UndoButton" Style="{StaticResource DangerButton}" Content="Undo Selected"/>
                    <Button x:Name="ExportLogButton" Style="{StaticResource SecondaryButton}" Content="Export Log"/>
                </WrapPanel>
            </DockPanel>
        </Border>

        <Grid Grid.Row="2">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            <ProgressBar x:Name="BusyBar" Grid.Row="0" Height="4" Visibility="Collapsed" Margin="0,0,0,8"/>
            <TabControl x:Name="TweakTabs" Grid.Row="1"/>
        </Grid>

        <Border Grid.Row="3"
                Background="#080808"
                BorderBrush="#27272A"
                BorderThickness="1"
                CornerRadius="8"
                Padding="12"
                Margin="0,14,0,0">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <DockPanel Grid.Row="0" Margin="0,0,0,8">
                    <TextBlock Text="Progress log" Foreground="#F4F4F5" FontWeight="SemiBold"/>
                    <TextBlock x:Name="TweakCountText" DockPanel.Dock="Right" Text="0 selected" Foreground="#A1A1AA" FontSize="12"/>
                </DockPanel>
                <TextBox x:Name="LogBox"
                         Grid.Row="1"
                         Background="#050505"
                         Foreground="#D4D4D8"
                         BorderBrush="#27272A"
                         BorderThickness="1"
                         FontFamily="Consolas"
                         FontSize="12"
                         IsReadOnly="True"
                         TextWrapping="Wrap"
                         VerticalScrollBarVisibility="Auto"
                         Padding="10"/>
            </Grid>
        </Border>
    </Grid>
</Window>
"@

[xml]$xamlDocument = $xaml
$reader = New-Object System.Xml.XmlNodeReader $xamlDocument
$script:Window = [Windows.Markup.XamlReader]::Load($reader)

$script:TweakTabs = $script:Window.FindName('TweakTabs')
$script:LogBox = $script:Window.FindName('LogBox')
$script:AdminStatus = $script:Window.FindName('AdminStatus')
$script:TweakCountText = $script:Window.FindName('TweakCountText')
$script:BusyBar = $script:Window.FindName('BusyBar')
$script:PreviewOnlyCheckBox = $script:Window.FindName('PreviewOnlyCheckBox')
$script:CreateRestorePointCheckBox = $script:Window.FindName('CreateRestorePointCheckBox')
$script:LoadProfileButton = $script:Window.FindName('LoadProfileButton')
$script:SaveProfileButton = $script:Window.FindName('SaveProfileButton')
$script:SelectRecommendedButton = $script:Window.FindName('SelectRecommendedButton')
$script:ClearSelectionButton = $script:Window.FindName('ClearSelectionButton')
$script:RefreshStateButton = $script:Window.FindName('RefreshStateButton')
$script:ApplyButton = $script:Window.FindName('ApplyButton')
$script:UndoButton = $script:Window.FindName('UndoButton')
$script:ExportLogButton = $script:Window.FindName('ExportLogButton')

Build-mpwareTabs
Update-mpwareAdminStatus
Update-mpwareSelectedCount

$script:LoadProfileButton.Add_Click({ Load-mpwareProfile })
$script:SaveProfileButton.Add_Click({ Save-mpwareProfile })
$script:SelectRecommendedButton.Add_Click({ Select-mpwareRecommended })
$script:ClearSelectionButton.Add_Click({ Clear-mpwareSelection })
$script:RefreshStateButton.Add_Click({ Update-mpwareStates })
$script:ApplyButton.Add_Click({ Invoke-mpwareSelection -Mode 'Apply' })
$script:UndoButton.Add_Click({ Invoke-mpwareSelection -Mode 'Undo' })
$script:ExportLogButton.Add_Click({ Export-mpwareLog })
$script:Window.Add_ContentRendered({
    Write-mpwareLog 'mpware ready. Hover a tweak to see exactly what it changes.'
    Write-mpwareLog 'Preview only is enabled by default; uncheck it when you are ready to apply real changes.'
    Update-mpwareStates
})

[void]$script:Window.ShowDialog()


