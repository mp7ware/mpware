Set-StrictMode -Version 2.0

$script:mpwareRoot = Split-Path -Parent $PSScriptRoot
$script:mpwareStateDir = Join-Path $script:mpwareRoot 'state'
$script:mpwareUndoPath = Join-Path $script:mpwareStateDir 'undo-state.json'

if (-not (Test-Path -LiteralPath $script:mpwareStateDir)) {
    New-Item -ItemType Directory -Path $script:mpwareStateDir -Force | Out-Null
}

function Write-mpwareTweakLog {
    param(
        [scriptblock]$Log,
        [string]$Message
    )

    if ($Log) {
        & $Log $Message
    }
    else {
        Write-Host $Message
    }
}

function ConvertTo-mpwareHashtable {
    param([object]$InputObject)

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $hash = @{}
        foreach ($key in $InputObject.Keys) {
            $hash[$key] = ConvertTo-mpwareHashtable $InputObject[$key]
        }
        return $hash
    }

    if ($InputObject -is [pscustomobject]) {
        $hash = @{}
        foreach ($property in $InputObject.PSObject.Properties) {
            $hash[$property.Name] = ConvertTo-mpwareHashtable $property.Value
        }
        return $hash
    }

    if (($InputObject -is [System.Collections.IEnumerable]) -and ($InputObject -isnot [string])) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += ConvertTo-mpwareHashtable $item
        }
        return ,$items
    }

    return $InputObject
}

function Get-mpwareUndoState {
    if (-not (Test-Path -LiteralPath $script:mpwareUndoPath)) {
        return @{}
    }

    try {
        $raw = Get-Content -LiteralPath $script:mpwareUndoPath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return @{}
        }

        $state = ConvertTo-mpwareHashtable ($raw | ConvertFrom-Json)
        if ($null -eq $state) {
            return @{}
        }

        return $state
    }
    catch {
        return @{}
    }
}

function Save-mpwareUndoState {
    param([hashtable]$State)

    if (-not (Test-Path -LiteralPath $script:mpwareStateDir)) {
        New-Item -ItemType Directory -Path $script:mpwareStateDir -Force | Out-Null
    }

    $State | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $script:mpwareUndoPath -Encoding UTF8
}

function Save-mpwareTweakSnapshot {
    param(
        [string]$TweakId,
        [hashtable]$Snapshot
    )

    $state = Get-mpwareUndoState
    $state[$TweakId] = [ordered]@{
        capturedAt = (Get-Date).ToString('o')
        snapshot   = $Snapshot
    }
    Save-mpwareUndoState -State $state
}

function Get-mpwareTweakSnapshot {
    param([string]$TweakId)

    $state = Get-mpwareUndoState
    if ($state.ContainsKey($TweakId)) {
        return $state[$TweakId]
    }

    return $null
}

function Get-mpwareRegistryValue {
    param(
        [string]$Path,
        [string]$Name,
        [object]$Default = $null
    )

    $key = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($key -and ($key.GetValueNames() -contains $Name)) {
        return $key.GetValue($Name)
    }

    return $Default
}

function Get-mpwareRegistryValueSnapshot {
    param(
        [string]$Path,
        [string]$Name
    )

    $snapshot = [ordered]@{
        Path   = $Path
        Name   = $Name
        Exists = $false
        Value  = $null
        Kind   = 'String'
    }

    $key = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($key -and ($key.GetValueNames() -contains $Name)) {
        $snapshot.Exists = $true
        $snapshot.Value = $key.GetValue($Name)
        $snapshot.Kind = $key.GetValueKind($Name).ToString()
    }

    return [pscustomobject]$snapshot
}

function Invoke-mpwareRegistrySet {
    param(
        [string]$TweakId,
        [array]$Entries,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        foreach ($entry in $Entries) {
            Write-mpwareTweakLog $Log "[preview] Set $($entry.Path)\$($entry.Name) to $($entry.Value)"
        }
        return
    }

    $snapshots = @()
    foreach ($entry in $Entries) {
        $snapshots += Get-mpwareRegistryValueSnapshot -Path $entry.Path -Name $entry.Name
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind    = 'Registry'
        Entries = $snapshots
    }

    foreach ($entry in $Entries) {
        if (-not (Test-Path -LiteralPath $entry.Path)) {
            New-Item -Path $entry.Path -Force | Out-Null
        }

        New-ItemProperty -LiteralPath $entry.Path -Name $entry.Name -Value $entry.Value -PropertyType $entry.Type -Force | Out-Null
        Write-mpwareTweakLog $Log "Set $($entry.Path)\$($entry.Name) to $($entry.Value)"
    }
}

function Restore-mpwareRegistrySet {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $state = Get-mpwareTweakSnapshot -TweakId $TweakId
    if (-not $state) {
        Write-mpwareTweakLog $Log "No saved restore data for $TweakId"
        return
    }

    foreach ($entry in @($state.snapshot.Entries)) {
        if ($DryRun) {
            if ($entry.Exists) {
                Write-mpwareTweakLog $Log "[preview] Restore $($entry.Path)\$($entry.Name) to $($entry.Value)"
            }
            else {
                Write-mpwareTweakLog $Log "[preview] Remove $($entry.Path)\$($entry.Name)"
            }
            continue
        }

        if ($entry.Exists) {
            if (-not (Test-Path -LiteralPath $entry.Path)) {
                New-Item -Path $entry.Path -Force | Out-Null
            }

            New-ItemProperty -LiteralPath $entry.Path -Name $entry.Name -Value $entry.Value -PropertyType $entry.Kind -Force | Out-Null
            Write-mpwareTweakLog $Log "Restored $($entry.Path)\$($entry.Name)"
        }
        else {
            Remove-ItemProperty -LiteralPath $entry.Path -Name $entry.Name -ErrorAction SilentlyContinue
            Write-mpwareTweakLog $Log "Removed restored-empty value $($entry.Path)\$($entry.Name)"
        }
    }
}

function Test-mpwareRegistryEntriesApplied {
    param([array]$Entries)

    $matches = 0
    foreach ($entry in $Entries) {
        $actual = Get-mpwareRegistryValue -Path $entry.Path -Name $entry.Name -Default $null
        if ([string]$actual -eq [string]$entry.Value) {
            $matches++
        }
    }

    if ($matches -eq $Entries.Count) {
        return 'Applied'
    }
    elseif ($matches -gt 0) {
        return 'Partial'
    }

    return 'Not applied'
}

function Convert-mpwareServiceStartMode {
    param([string]$StartMode)

    switch ($StartMode) {
        'Auto' { return 'Automatic' }
        'Automatic' { return 'Automatic' }
        'Manual' { return 'Manual' }
        'Disabled' { return 'Disabled' }
        default { return 'Manual' }
    }
}

function Get-mpwareServiceSnapshot {
    param([string]$Name)

    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    if (-not $service) {
        return [pscustomobject]@{
            Name      = $Name
            Exists    = $false
            StartMode = $null
            State     = $null
        }
    }

    return [pscustomobject]@{
        Name      = $Name
        Exists    = $true
        StartMode = $service.StartMode
        State     = $service.State
    }
}

function Invoke-mpwareServiceStartup {
    param(
        [string]$TweakId,
        [array]$Services,
        [string]$StartupType,
        [bool]$StopRunning,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        foreach ($serviceName in $Services) {
            Write-mpwareTweakLog $Log "[preview] Set service $serviceName startup to $StartupType"
        }
        return
    }

    $snapshots = @()
    foreach ($serviceName in $Services) {
        $snapshots += Get-mpwareServiceSnapshot -Name $serviceName
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind     = 'Services'
        Services = $snapshots
    }

    foreach ($serviceName in $Services) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if (-not $service) {
            Write-mpwareTweakLog $Log "Service $serviceName was not found; skipped"
            continue
        }

        if ($StopRunning -and $service.Status -eq 'Running') {
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        }

        Set-Service -Name $serviceName -StartupType $StartupType -ErrorAction Stop
        Write-mpwareTweakLog $Log "Set service $serviceName startup to $StartupType"
    }
}

function Restore-mpwareServiceStartup {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $state = Get-mpwareTweakSnapshot -TweakId $TweakId
    if (-not $state) {
        Write-mpwareTweakLog $Log "No saved restore data for $TweakId"
        return
    }

    foreach ($service in @($state.snapshot.Services)) {
        if (-not $service.Exists) {
            Write-mpwareTweakLog $Log "Service $($service.Name) did not exist before; skipped"
            continue
        }

        $startupType = Convert-mpwareServiceStartMode -StartMode $service.StartMode
        if ($DryRun) {
            Write-mpwareTweakLog $Log "[preview] Restore service $($service.Name) startup to $startupType"
            continue
        }

        Set-Service -Name $service.Name -StartupType $startupType -ErrorAction SilentlyContinue
        Write-mpwareTweakLog $Log "Restored service $($service.Name) startup to $startupType"
    }
}

function Test-mpwareServicesStartupType {
    param(
        [array]$Services,
        [string]$StartupType
    )

    $matches = 0
    foreach ($serviceName in $Services) {
        $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
        if ($service -and (Convert-mpwareServiceStartMode $service.StartMode) -eq $StartupType) {
            $matches++
        }
    }

    if ($matches -eq $Services.Count) {
        return 'Applied'
    }
    elseif ($matches -gt 0) {
        return 'Partial'
    }

    return 'Not applied'
}

function Get-mpwareTaskSnapshot {
    param(
        [string]$TaskPath,
        [string]$TaskName
    )

    try {
        $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction Stop
        return [pscustomobject]@{
            TaskPath = $TaskPath
            TaskName = $TaskName
            Exists   = $true
            Enabled  = [bool]$task.Settings.Enabled
        }
    }
    catch {
        return [pscustomobject]@{
            TaskPath = $TaskPath
            TaskName = $TaskName
            Exists   = $false
            Enabled  = $null
        }
    }
}

function Invoke-mpwareTaskDisable {
    param(
        [string]$TweakId,
        [array]$Tasks,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        foreach ($task in $Tasks) {
            Write-mpwareTweakLog $Log "[preview] Disable scheduled task $($task.TaskPath)$($task.TaskName)"
        }
        return
    }

    $snapshots = @()
    foreach ($task in $Tasks) {
        $snapshots += Get-mpwareTaskSnapshot -TaskPath $task.TaskPath -TaskName $task.TaskName
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind  = 'ScheduledTasks'
        Tasks = $snapshots
    }

    foreach ($task in $Tasks) {
        try {
            Disable-ScheduledTask -TaskPath $task.TaskPath -TaskName $task.TaskName -ErrorAction Stop | Out-Null
            Write-mpwareTweakLog $Log "Disabled scheduled task $($task.TaskPath)$($task.TaskName)"
        }
        catch {
            Write-mpwareTweakLog $Log "Scheduled task $($task.TaskPath)$($task.TaskName) was not found; skipped"
        }
    }
}

function Restore-mpwareTaskDisable {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $state = Get-mpwareTweakSnapshot -TweakId $TweakId
    if (-not $state) {
        Write-mpwareTweakLog $Log "No saved restore data for $TweakId"
        return
    }

    foreach ($task in @($state.snapshot.Tasks)) {
        if (-not $task.Exists) {
            Write-mpwareTweakLog $Log "Scheduled task $($task.TaskPath)$($task.TaskName) did not exist before; skipped"
            continue
        }

        if ($DryRun) {
            Write-mpwareTweakLog $Log "[preview] Restore scheduled task $($task.TaskPath)$($task.TaskName) enabled=$($task.Enabled)"
            continue
        }

        if ($task.Enabled) {
            Enable-ScheduledTask -TaskPath $task.TaskPath -TaskName $task.TaskName -ErrorAction SilentlyContinue | Out-Null
        }
        else {
            Disable-ScheduledTask -TaskPath $task.TaskPath -TaskName $task.TaskName -ErrorAction SilentlyContinue | Out-Null
        }

        Write-mpwareTweakLog $Log "Restored scheduled task $($task.TaskPath)$($task.TaskName)"
    }
}

function Test-mpwareTasksDisabled {
    param([array]$Tasks)

    $matches = 0
    foreach ($task in $Tasks) {
        try {
            $scheduledTask = Get-ScheduledTask -TaskPath $task.TaskPath -TaskName $task.TaskName -ErrorAction Stop
            if (-not [bool]$scheduledTask.Settings.Enabled) {
                $matches++
            }
        }
        catch {
        }
    }

    if ($matches -eq $Tasks.Count) {
        return 'Applied'
    }
    elseif ($matches -gt 0) {
        return 'Partial'
    }

    return 'Not applied'
}

function Get-mpwareActivePowerScheme {
    $output = & powercfg /getactivescheme 2>$null
    $text = ($output | Out-String)
    if ($text -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        return $Matches[1]
    }

    return $null
}

function Invoke-mpwareUltimatePowerPlan {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $ultimateGuid = 'e9a42b02-d5df-448d-aa00-03f14749eb61'
    if ($DryRun) {
        Write-mpwareTweakLog $Log "[preview] Duplicate and activate the Ultimate Performance power scheme"
        return
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind         = 'PowerPlan'
        ActiveScheme = (Get-mpwareActivePowerScheme)
    }

    $output = & powercfg -duplicatescheme $ultimateGuid 2>&1
    $text = ($output | Out-String)
    $schemeToActivate = $ultimateGuid
    if ($text -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        $schemeToActivate = $Matches[1]
    }

    & powercfg /setactive $schemeToActivate | Out-Null
    Write-mpwareTweakLog $Log "Activated Ultimate Performance power scheme"
}

function Restore-mpwareUltimatePowerPlan {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $state = Get-mpwareTweakSnapshot -TweakId $TweakId
    if (-not $state -or -not $state.snapshot.ActiveScheme) {
        Write-mpwareTweakLog $Log "No saved power plan restore data for $TweakId"
        return
    }

    if ($DryRun) {
        Write-mpwareTweakLog $Log "[preview] Restore active power scheme to $($state.snapshot.ActiveScheme)"
        return
    }

    & powercfg /setactive $state.snapshot.ActiveScheme | Out-Null
    Write-mpwareTweakLog $Log "Restored previous active power scheme"
}

function Test-mpwareUltimatePowerPlan {
    $output = & powercfg /getactivescheme 2>$null
    $text = ($output | Out-String)
    if ($text -match 'Ultimate Performance') {
        return 'Applied'
    }

    return 'Not applied'
}

function Invoke-mpwareAppxRemoval {
    param(
        [string]$TweakId,
        [array]$PackageNames,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        foreach ($packageName in $PackageNames) {
            $packages = @(Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue)
            if ($packages.Count -gt 0) {
                Write-mpwareTweakLog $Log "[preview] Remove AppX package $packageName for the current user"
            }
            else {
                Write-mpwareTweakLog $Log "[preview] AppX package $packageName is not installed for the current user"
            }
        }
        return
    }

    $installed = @()
    foreach ($packageName in $PackageNames) {
        $installed += @(Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Select-Object Name, PackageFullName)
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind     = 'AppxRemoval'
        Packages = $installed
    }

    foreach ($package in $installed) {
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction SilentlyContinue
        Write-mpwareTweakLog $Log "Removed AppX package $($package.Name) for the current user"
    }

    if ($installed.Count -eq 0) {
        Write-mpwareTweakLog $Log "No matching Teams AppX packages were installed for the current user"
    }
}

function Restore-mpwareTeamsPersonal {
    param(
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        Write-mpwareTweakLog $Log "[preview] Reinstall Microsoft Teams with winget if available"
        return
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-mpwareTweakLog $Log "winget is not available. Reinstall Microsoft Teams from the Microsoft Store."
        return
    }

    & winget install --id Microsoft.Teams -e --source winget --accept-package-agreements --accept-source-agreements
    Write-mpwareTweakLog $Log "Requested Microsoft Teams reinstall through winget"
}

function Test-mpwareAppxRemoved {
    param([array]$PackageNames)

    foreach ($packageName in $PackageNames) {
        if (Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue) {
            return 'Not applied'
        }
    }

    return 'Applied'
}

function Invoke-mpwareBlockedAction {
    param(
        [string]$Name,
        [string]$Reason,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    Write-mpwareTweakLog $Log "Blocked: $Name"
    Write-mpwareTweakLog $Log $Reason
    if ($DryRun) {
        Write-mpwareTweakLog $Log '[preview] No changes would be made.'
    }
}

function Invoke-mpwareLogOnlyAction {
    param(
        [string]$Name,
        [string[]]$Lines,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $prefix = if ($DryRun) { '[preview] ' } else { '' }
    Write-mpwareTweakLog $Log "$prefix$Name"
    foreach ($line in $Lines) {
        Write-mpwareTweakLog $Log "$prefix$line"
    }
}

function Invoke-mpwareCommandAction {
    param(
        [string]$TweakId,
        [string]$Name,
        [string]$FilePath,
        [string[]]$ArgumentList,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $commandLine = "$FilePath $($ArgumentList -join ' ')".Trim()
    if ($DryRun) {
        Write-mpwareTweakLog $Log "[preview] Run: $commandLine"
        return
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind    = 'Command'
        Command = $commandLine
    }

    Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -NoNewWindow
    Write-mpwareTweakLog $Log "Completed command action: $Name"
}

function Invoke-mpwareTempCleanup {
    param(
        [string]$TweakId,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $targets = @($env:TEMP, "$env:SystemRoot\Temp") | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    if ($DryRun) {
        foreach ($target in $targets) {
            Write-mpwareTweakLog $Log "[preview] Clean files under $target"
        }
        Write-mpwareTweakLog $Log '[preview] Empty recycle bin if supported.'
        return
    }

    Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
        Kind      = 'Cleanup'
        Targets   = $targets
        StartedAt = (Get-Date).ToString('o')
    }

    foreach ($target in $targets) {
        Get-ChildItem -LiteralPath $target -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-mpwareTweakLog $Log "Cleaned temporary files under $target"
    }

    Clear-RecycleBin -Force -ErrorAction SilentlyContinue
    Write-mpwareTweakLog $Log 'Recycle bin cleanup requested.'
}

function Invoke-mpwareRepairWindows {
    param(
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        Write-mpwareTweakLog $Log '[preview] Run DISM /Online /Cleanup-Image /RestoreHealth'
        Write-mpwareTweakLog $Log '[preview] Run sfc /scannow'
        return
    }

    Write-mpwareTweakLog $Log 'Starting DISM repair. This can take a while.'
    & dism.exe /Online /Cleanup-Image /RestoreHealth
    Write-mpwareTweakLog $Log 'Starting SFC scan. This can take a while.'
    & sfc.exe /scannow
    Write-mpwareTweakLog $Log 'Windows repair commands completed.'
}

function Invoke-mpwareRestartExplorer {
    param(
        [bool]$DryRun,
        [scriptblock]$Log
    )

    if ($DryRun) {
        Write-mpwareTweakLog $Log '[preview] Restart explorer.exe'
        return
    }

    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Process explorer.exe
    Write-mpwareTweakLog $Log 'Restarted explorer.exe'
}

function Invoke-mpwareWingetInstall {
    param(
        [string]$TweakId,
        [string[]]$PackageIds,
        [bool]$DryRun,
        [scriptblock]$Log
    )

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-mpwareTweakLog $Log 'winget is not available on this system.'
        return
    }

    foreach ($packageId in $PackageIds) {
        if ($DryRun) {
            Write-mpwareTweakLog $Log "[preview] winget install --id $packageId -e"
            continue
        }

        Save-mpwareTweakSnapshot -TweakId $TweakId -Snapshot @{
            Kind      = 'WingetInstall'
            PackageId = $packageId
        }
        & winget install --id $packageId -e --source winget --accept-package-agreements --accept-source-agreements
        Write-mpwareTweakLog $Log "Requested install for $packageId"
    }
}

function New-mpwareTweak {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Category,
        [string]$Description,
        [string]$ApplySummary,
        [string]$UndoSummary,
        [ValidateSet('Low', 'Medium', 'High')]
        [string]$Risk = 'Low',
        [bool]$RequiresAdmin = $false,
        [bool]$Recommended = $false,
        [scriptblock]$Apply,
        [scriptblock]$Undo,
        [scriptblock]$GetState
    )

    [pscustomobject]@{
        Id            = $Id
        Name          = $Name
        Category      = $Category
        Description   = $Description
        ApplySummary  = $ApplySummary
        UndoSummary   = $UndoSummary
        Risk          = $Risk
        RequiresAdmin = $RequiresAdmin
        Recommended   = $Recommended
        Apply         = $Apply.GetNewClosure()
        Undo          = $Undo.GetNewClosure()
        GetState      = $GetState.GetNewClosure()
    }
}

function New-mpwareBlockedTweak {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Category,
        [string]$Description,
        [string]$Reason
    )

    New-mpwareTweak `
        -Id $Id `
        -Name $Name `
        -Category $Category `
        -Risk 'High' `
        -RequiresAdmin $true `
        -Description $Description `
        -ApplySummary 'This upstream-style action is intentionally blocked in mpware.' `
        -UndoSummary 'No changes are made, so there is nothing to undo.' `
        -Apply {
            param([bool]$DryRun, [scriptblock]$Log)
            Invoke-mpwareBlockedAction -Name $Name -Reason $Reason -DryRun $DryRun -Log $Log
        } `
        -Undo {
            param([bool]$DryRun, [scriptblock]$Log)
            Invoke-mpwareLogOnlyAction -Name "Undo $Name" -Lines @('No changes were made by this blocked action.') -DryRun $DryRun -Log $Log
        } `
        -GetState {
            'Blocked'
        }
}

function New-mpwareLogOnlyTweak {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Category,
        [string]$Description,
        [string]$ApplySummary,
        [string[]]$Lines,
        [ValidateSet('Low', 'Medium', 'High')]
        [string]$Risk = 'Medium',
        [bool]$RequiresAdmin = $false
    )

    New-mpwareTweak `
        -Id $Id `
        -Name $Name `
        -Category $Category `
        -Risk $Risk `
        -RequiresAdmin $RequiresAdmin `
        -Description $Description `
        -ApplySummary $ApplySummary `
        -UndoSummary 'This feature is represented as a guided action; no automatic restore data is needed.' `
        -Apply {
            param([bool]$DryRun, [scriptblock]$Log)
            Invoke-mpwareLogOnlyAction -Name $Name -Lines $Lines -DryRun $DryRun -Log $Log
        } `
        -Undo {
            param([bool]$DryRun, [scriptblock]$Log)
            Invoke-mpwareLogOnlyAction -Name "Undo $Name" -Lines @('No automatic undo is required for this guided action.') -DryRun $DryRun -Log $Log
        } `
        -GetState {
            'Available'
        }
}

function Get-mpwareParityTweaks {
    $consumerAppx = @(
        'Clipchamp.Clipchamp',
        'Microsoft.BingNews',
        'Microsoft.BingWeather',
        'Microsoft.GetHelp',
        'Microsoft.Getstarted',
        'Microsoft.MicrosoftSolitaireCollection',
        'Microsoft.MicrosoftStickyNotes',
        'Microsoft.People',
        'Microsoft.PowerAutomateDesktop',
        'Microsoft.Todos',
        'Microsoft.WindowsFeedbackHub',
        'Microsoft.WindowsMaps',
        'Microsoft.WindowsSoundRecorder',
        'Microsoft.YourPhone',
        'Microsoft.ZuneMusic',
        'Microsoft.ZuneVideo',
        'MicrosoftCorporationII.QuickAssist'
    )
    $xboxAppx = @(
        'Microsoft.GamingApp',
        'Microsoft.Xbox.TCUI',
        'Microsoft.XboxApp',
        'Microsoft.XboxGameOverlay',
        'Microsoft.XboxGamingOverlay',
        'Microsoft.XboxIdentityProvider',
        'Microsoft.XboxSpeechToTextOverlay'
    )
    $storeAppx = @('Microsoft.WindowsStore', 'Microsoft.StorePurchaseApp')
    $teamsOneDriveAppx = @('MSTeams', 'MicrosoftTeams', 'Microsoft.OneDriveSync')
    $edgeAppx = @('Microsoft.MicrosoftEdge.Stable')
    $allDebloatAppx = @($consumerAppx + $xboxAppx + $teamsOneDriveAppx)
    $debloatKeepStoreXboxAppx = @($consumerAppx + $teamsOneDriveAppx)
    $debloatKeepEdgeAppx = @($consumerAppx + $xboxAppx + $teamsOneDriveAppx)
    $debloatKeepStoreAppx = @($consumerAppx + $xboxAppx + $teamsOneDriveAppx + $edgeAppx)

    $telemetryTasks = @(
        @{ TaskPath = '\Microsoft\Windows\Application Experience\'; TaskName = 'Microsoft Compatibility Appraiser' },
        @{ TaskPath = '\Microsoft\Windows\Application Experience\'; TaskName = 'ProgramDataUpdater' },
        @{ TaskPath = '\Microsoft\Windows\Customer Experience Improvement Program\'; TaskName = 'Consolidator' },
        @{ TaskPath = '\Microsoft\Windows\Customer Experience Improvement Program\'; TaskName = 'UsbCeip' },
        @{ TaskPath = '\Microsoft\Windows\DiskDiagnostic\'; TaskName = 'Microsoft-Windows-DiskDiagnosticDataCollector' }
    )

    $mpwareServices = @(
        'Fax',
        'RemoteRegistry',
        'MapsBroker',
        'lfsvc',
        'WpcMonSvc',
        'SCardSvr',
        'ScDeviceEnum',
        'SCPolicySvc',
        'WbioSrvc',
        'WalletService',
        'PhoneSvc',
        'RetailDemo',
        'SharedAccess',
        'TrkWks',
        'WerSvc',
        'wisvc',
        'WMPNetworkSvc',
        'XblAuthManager',
        'XblGameSave',
        'XboxGipSvc',
        'XboxNetApiSvc'
    )

    @(
        New-mpwareTweak `
            -Id 'gp-updates-notify' `
            -Name 'Group Policy: notify before Windows updates' `
            -Category 'Group Policy' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware uses a safer policy that stops surprise installs while keeping Windows Update available.' `
            -ApplySummary 'Sets Windows Update AU policy to notify before download/install.' `
            -UndoSummary 'Restores the previous policy values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name = 'NoAutoUpdate'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name = 'AUOptions'; Type = 'DWord'; Value = 2 }
                )
                Invoke-mpwareRegistrySet -TweakId 'gp-updates-notify' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'gp-updates-notify' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name = 'AUOptions'; Value = 2 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareBlockedTweak `
            -Id 'gp-defender-disable-blocked' `
            -Name 'Group Policy: disable Microsoft Defender' `
            -Category 'Group Policy' `
            -Description 'mpware shows the Defender-disable category for visibility but does not automate disabling core endpoint protection.' `
            -Reason 'Disabling Defender can leave the machine exposed. mpware will not ship a one-click action that turns off built-in security protection.'

        New-mpwareTweak `
            -Id 'gp-telemetry-limit' `
            -Name 'Group Policy: limit telemetry bundle' `
            -Category 'Group Policy' `
            -Risk 'Low' `
            -RequiresAdmin $true `
            -Description 'Applies telemetry-reduction settings without hosts-file blocking.' `
            -ApplySummary 'Limits optional diagnostic data and disables selected CEIP/appraiser scheduled tasks.' `
            -UndoSummary 'Restores the policy registry values and task states captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Type = 'DWord'; Value = 1 },
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'LimitDiagnosticLogCollection'; Type = 'DWord'; Value = 1 },
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'gp-telemetry-limit-registry' -Entries $entries -DryRun $DryRun -Log $Log
                Invoke-mpwareTaskDisable -TweakId 'gp-telemetry-limit-tasks' -Tasks $telemetryTasks -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'gp-telemetry-limit-registry' -DryRun $DryRun -Log $Log
                Restore-mpwareTaskDisable -TweakId 'gp-telemetry-limit-tasks' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Value = 1 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'tasks-telemetry-bundle' `
            -Name 'Remove/disable telemetry scheduled tasks' `
            -Category 'Scheduled Tasks' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware disables a targeted telemetry-related task bundle and saves restore state.' `
            -ApplySummary 'Disables selected Application Experience, CEIP, and DiskDiagnostic scheduled tasks.' `
            -UndoSummary 'Restores each task enabled state captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareTaskDisable -TweakId 'tasks-telemetry-bundle' -Tasks $telemetryTasks -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareTaskDisable -TweakId 'tasks-telemetry-bundle' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareTasksDisabled -Tasks $telemetryTasks
            }

        New-mpwareTweak `
            -Id 'services-mpware-manual' `
            -Name 'mpware service cleanup' `
            -Category 'Services' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Sets the non-core services commonly adjusted for lean Windows installs to Manual instead of hard-disabling everything.' `
            -ApplySummary 'Changes selected service startup types to Manual and records previous states.' `
            -UndoSummary 'Restores each captured service startup type.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareServiceStartup -TweakId 'services-mpware-manual' -Services $mpwareServices -StartupType 'Manual' -StopRunning $false -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareServiceStartup -TweakId 'services-mpware-manual' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareServicesStartupType -Services $mpwareServices -StartupType 'Manual'
            }

        New-mpwareTweak `
            -Id 'debloat-preset-all' `
            -Name 'Debloat preset: all non-core apps' `
            -Category 'Debloat' `
            -Risk 'High' `
            -Description 'Uses a safer current-user AppX removal path. Edge and Store removal are not forced.' `
            -ApplySummary 'Removes common consumer, Xbox, Teams, and OneDrive AppX packages for the current user.' `
            -UndoSummary 'AppX undo is package-dependent; reinstall from Microsoft Store or winget as needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-preset-all' -PackageNames $allDebloatAppx -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Restore debloat preset' -Lines @('Reinstall removed packages from Microsoft Store or winget if needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $allDebloatAppx
            }

        New-mpwareTweak `
            -Id 'debloat-preset-keep-store-xbox-edge' `
            -Name 'Debloat preset: keep Store, Xbox, Edge' `
            -Category 'Debloat' `
            -Risk 'Medium' `
            -Description 'mpware preset that removes consumer apps while keeping Store, Xbox, and Edge.' `
            -ApplySummary 'Removes common consumer AppX packages for the current user.' `
            -UndoSummary 'Reinstall removed packages from Microsoft Store or winget if needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-preset-keep-store-xbox-edge' -PackageNames $consumerAppx -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Restore consumer apps' -Lines @('Reinstall removed packages from Microsoft Store or winget if needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $consumerAppx
            }

        New-mpwareTweak `
            -Id 'debloat-preset-keep-store-xbox' `
            -Name 'Debloat preset: keep Store and Xbox' `
            -Category 'Debloat' `
            -Risk 'Medium' `
            -Description 'mpware preset that keeps Store and Xbox while removing consumer apps and Teams/OneDrive AppX entries.' `
            -ApplySummary 'Removes consumer and Teams/OneDrive AppX packages for the current user.' `
            -UndoSummary 'Reinstall removed packages from Microsoft Store or winget if needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-preset-keep-store-xbox' -PackageNames $debloatKeepStoreXboxAppx -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Restore debloated apps' -Lines @('Reinstall removed packages from Microsoft Store or winget if needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $debloatKeepStoreXboxAppx
            }

        New-mpwareTweak `
            -Id 'debloat-preset-keep-edge' `
            -Name 'Debloat preset: keep Edge' `
            -Category 'Debloat' `
            -Risk 'High' `
            -Description 'mpware preset that keeps Edge while removing consumer, Xbox, and Teams/OneDrive AppX entries.' `
            -ApplySummary 'Removes consumer, Xbox, and Teams/OneDrive AppX packages for the current user.' `
            -UndoSummary 'Reinstall removed packages from Microsoft Store or winget if needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-preset-keep-edge' -PackageNames $debloatKeepEdgeAppx -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Restore debloated apps' -Lines @('Reinstall removed packages from Microsoft Store or winget if needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $debloatKeepEdgeAppx
            }

        New-mpwareTweak `
            -Id 'debloat-preset-keep-store' `
            -Name 'Debloat preset: keep Store' `
            -Category 'Debloat' `
            -Risk 'High' `
            -Description 'mpware preset that keeps Store while removing consumer, Xbox, and Teams/OneDrive AppX entries.' `
            -ApplySummary 'Removes consumer, Xbox, and Teams/OneDrive AppX packages for the current user.' `
            -UndoSummary 'Reinstall removed packages from Microsoft Store or winget if needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-preset-keep-store' -PackageNames $debloatKeepStoreAppx -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Restore debloated apps' -Lines @('Reinstall removed packages from Microsoft Store or winget if needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $debloatKeepStoreAppx
            }

        New-mpwareLogOnlyTweak `
            -Id 'debloat-custom-picker' `
            -Name 'Custom debloat picker' `
            -Category 'Debloat' `
            -Risk 'Medium' `
            -Description 'Guided placeholder for mpware custom AppX/capability/package picker.' `
            -ApplySummary 'Lists the custom picker workflow that should become a dedicated picker dialog.' `
            -Lines @('Planned: enumerate AppX packages, capabilities, optional features, and installed programs in a selectable dialog.', 'Current build: use individual debloat presets or add a new package array in mpware.tweaks.ps1.')

        New-mpwareTweak `
            -Id 'optional-black-theme' `
            -Name 'Optional: black Windows theme' `
            -Category 'Optional' `
            -Risk 'Low' `
            -Description 'Applies the mpware black theme idea with Windows dark app/system mode and black accent settings.' `
            -ApplySummary 'Sets current-user theme and accent registry values.' `
            -UndoSummary 'Restores the previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'AppsUseLightTheme'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'SystemUsesLightTheme'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\DWM'; Name = 'ColorPrevalence'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'optional-black-theme' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'optional-black-theme' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'AppsUseLightTheme'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'SystemUsesLightTheme'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'optional-no-driver-updates' `
            -Name 'Optional: exclude drivers from Windows Update' `
            -Category 'Optional' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Prevents driver delivery through Windows Update, matching one of mpware optional update controls.' `
            -ApplySummary 'Sets ExcludeWUDriversInQualityUpdate policy to 1.' `
            -UndoSummary 'Restores the previous policy value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'; Name = 'ExcludeWUDriversInQualityUpdate'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'optional-no-driver-updates' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'optional-no-driver-updates' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'; Name = 'ExcludeWUDriversInQualityUpdate'; Value = 1 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'optional-fast-shutdown' `
            -Name 'Optional: fast shutdown/restart' `
            -Category 'Optional' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Reduces shutdown wait time and auto-ends foreground apps, inspired by mpware optional tweaks.' `
            -ApplySummary 'Sets WaitToKillServiceTimeout, AutoEndTasks, HungAppTimeout, and WaitToKillAppTimeout.' `
            -UndoSummary 'Restores previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SYSTEM\CurrentControlSet\Control'; Name = 'WaitToKillServiceTimeout'; Type = 'String'; Value = '2000' },
                    @{ Path = 'HKCU:\Control Panel\Desktop'; Name = 'AutoEndTasks'; Type = 'String'; Value = '1' },
                    @{ Path = 'HKCU:\Control Panel\Desktop'; Name = 'HungAppTimeout'; Type = 'String'; Value = '2000' },
                    @{ Path = 'HKCU:\Control Panel\Desktop'; Name = 'WaitToKillAppTimeout'; Type = 'String'; Value = '2000' }
                )
                Invoke-mpwareRegistrySet -TweakId 'optional-fast-shutdown' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'optional-fast-shutdown' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Control Panel\Desktop'; Name = 'AutoEndTasks'; Value = '1' }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareLogOnlyTweak `
            -Id 'optional-advanced-bundle' `
            -Name 'Optional: advanced tweak bundle' `
            -Category 'Optional' `
            -Risk 'High' `
            -RequiresAdmin $true `
            -Description 'Tracks the rest of mpware optional tweaks: transparent taskbar, Razer/ASUS blocking, PBO startup, PowerShell logging, no GUI boot, backup app removal, time server, mouse acceleration, and device encryption.' `
            -ApplySummary 'Logs the advanced optional actions for manual review before implementing them one by one.' `
            -Lines @('Covered upstream options: transparent taskbar, remove network icon, recycle-bin label cleanup, remove mouse/sound schemes, hide user tile, modern cursor, dark/classic accents, update deferrals, block OEM download servers, PBO startup, no GUI boot, Game Bar popup, Backup app removal, time server, desktop mouse accel, device encryption.', 'These should be split into individual reversible registry/service/package actions before enabling automatic apply.')

        New-mpwareLogOnlyTweak `
            -Id 'context-add-bundle' `
            -Name 'Context menu: add tools bundle' `
            -Category 'Context Menu' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for add-to-context-menu actions.' `
            -ApplySummary 'Documents context entries to add: new script files, PowerShell options, snipping, shutdown, run as admin, CMD/PowerShell, kill tasks, permanent delete, and take ownership.' `
            -Lines @('Planned add entries: New .reg/.ps1/.bat files, PS1 open/run options, Snipping Tool, Shutdown, Run as Admin for scripts, Open CMD/PowerShell, Kill Not Responding Tasks, Delete Permanently, Take Ownership.', 'Context-menu changes are registry-heavy and should be implemented as separate reversible actions.')

        New-mpwareLogOnlyTweak `
            -Id 'context-remove-bundle' `
            -Name 'Context menu: remove clutter bundle' `
            -Category 'Context Menu' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for remove-from-context-menu actions.' `
            -ApplySummary 'Documents context entries to remove: favorites, customize, give access, terminal, previous versions, print, send to, share, personalize, display, extract all, compatibility, and library.' `
            -Lines @('Planned remove entries: Add to Favorites, Customize Folder, Give Access To, Open in Terminal, Restore Previous Versions, Print, Send To, Share, Personalize, Display Settings, Extract All, Troubleshoot Compatibility, Include in Library.', 'These should be implemented as separate reversible registry actions.')

        New-mpwareLogOnlyTweak `
            -Id 'power-import-custom' `
            -Name 'Power plans: import custom plan' `
            -Category 'Power Plans' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for importing a custom performance power plan.' `
            -ApplySummary 'Logs the custom plan import workflow until a .pow plan is bundled.' `
            -Lines @('Planned: include a .pow plan, import with powercfg -import, capture previous active scheme, then activate the imported plan.')

        New-mpwareLogOnlyTweak `
            -Id 'power-enable-hidden-overlays' `
            -Name 'Power plans: enable hidden overlay plans' `
            -Category 'Power Plans' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for hidden plans: Ultimate Performance, Max Performance Overlay, and High Performance Overlay.' `
            -ApplySummary 'Ultimate Performance is implemented separately; this action tracks the remaining overlay plan work.' `
            -Lines @('Ultimate Performance is available in the Performance tab.', 'Planned: add Max Performance Overlay and High Performance Overlay activation with previous-plan restore.')

        New-mpwareLogOnlyTweak `
            -Id 'power-usb-saving' `
            -Name 'Power plans: USB power saving picker' `
            -Category 'Power Plans' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for USB hub/device power saving tweaks.' `
            -ApplySummary 'Logs the USB device picker workflow.' `
            -Lines @('Planned: enumerate USB hubs/devices, show a picker, and disable Allow the computer to turn off this device to save power with restore data.')

        New-mpwareLogOnlyTweak `
            -Id 'w11-explorer-patcher-bundle' `
            -Name 'Windows 11: Explorer patch bundle' `
            -Category 'Windows 11' `
            -Risk 'High' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for ExplorerPatcher/OpenShell/rounded-corner/Win10 shell restoration actions.' `
            -ApplySummary 'Logs the shell patch workflow instead of silently installing third-party shell patchers.' `
            -Lines @('Covered upstream options: remove rounded edges, Windows 10 taskbar/start menu, Windows 10 Explorer ribbon, replace Start/Search with OpenShell.', 'mpware will not silently download shell patchers; add vendor links or bundled checksums before enabling.')

        New-mpwareTweak `
            -Id 'w11-hide-settings-ads' `
            -Name 'Windows 11: hide Settings ads' `
            -Category 'Windows 11' `
            -Risk 'Low' `
            -Description 'Hides several Windows 11 Settings/Home recommendation surfaces.' `
            -ApplySummary 'Sets current-user Settings recommendation values to 0.' `
            -UndoSummary 'Restores the previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338393Enabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353694Enabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'ShowSyncProviderNotifications'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'w11-hide-settings-ads' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'w11-hide-settings-ads' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'ShowSyncProviderNotifications'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'w11-small-taskbar-icons' `
            -Name 'Windows 11: small taskbar icons' `
            -Category 'Windows 11' `
            -Risk 'Medium' `
            -Description 'Applies the TaskbarSi registry value used by many Windows 11 taskbar size tweaks. Effect depends on Windows build.' `
            -ApplySummary 'Sets TaskbarSi to 0 for the current user.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarSi'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'w11-small-taskbar-icons' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'w11-small-taskbar-icons' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarSi'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareLogOnlyTweak `
            -Id 'w11-restore-win10-assets' `
            -Name 'Windows 11: restore Windows 10 assets bundle' `
            -Category 'Windows 11' `
            -Risk 'High' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for Win10 recycle bin, snipping tool, task manager, notepad, icons, sounds, dark winver, quick settings, labels, mouse throttle, and Start menu variants.' `
            -ApplySummary 'Logs the Windows 10 asset restoration workflow.' `
            -Lines @('Covered upstream options: Win10 recycle bin icon, Snipping Tool, Task Manager wrapper, legacy Notepad, Win10 icons, Win10 sounds, dark winver, quick settings tiles, system labels, mouse throttle, new Start menu toggles.', 'These need bundled assets/checksums and individual restore paths before automatic apply.')

        New-mpwareTweak `
            -Id 'install-packages-runtime' `
            -Name 'Install packages: runtimes essentials' `
            -Category 'Install' `
            -Risk 'Medium' `
            -Description 'mpware can install DirectX, Visual C++ runtimes, and .NET 3.5. mpware starts with winget-based runtime helpers where available.' `
            -ApplySummary 'Requests winget installs for common Visual C++ runtime packages.' `
            -UndoSummary 'Installed runtime packages are managed by Windows Apps/Settings.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareWingetInstall -TweakId 'install-packages-runtime' -PackageIds @('Microsoft.VCRedist.2015+.x64', 'Microsoft.DotNet.DesktopRuntime.8') -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Runtime package undo' -Lines @('Uninstall runtimes through Windows Settings if you no longer want them.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareTweak `
            -Id 'install-browsers' `
            -Name 'Install browsers' `
            -Category 'Install' `
            -Risk 'Low' `
            -Description 'Feature parity for the mpware browser installer.' `
            -ApplySummary 'Requests winget installs for Firefox and Google Chrome.' `
            -UndoSummary 'Browsers are removed through Windows Settings or winget uninstall.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareWingetInstall -TweakId 'install-browsers' -PackageIds @('Mozilla.Firefox', 'Google.Chrome') -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Browser install undo' -Lines @('Uninstall browsers through Windows Settings or winget uninstall.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareLogOnlyTweak `
            -Id 'install-network-driver' `
            -Name 'Install network driver helper' `
            -Category 'Install' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for network driver workflow.' `
            -ApplySummary 'Logs online/offline network driver search and QoS helper workflow.' `
            -Lines @('Planned: detect adapter vendor, link to vendor driver page or local driver pack, and offer reversible QoS settings.', 'mpware does not bundle drivers yet.')

        New-mpwareLogOnlyTweak `
            -Id 'install-nvidia-driver' `
            -Name 'Install NVIDIA driver helper' `
            -Category 'Install' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for NVIDIA installer and post-install tweaks.' `
            -ApplySummary 'Logs NVIDIA driver selection, strip-driver, HDCP, telemetry, MSI mode, vibrance, and monitor-speaker workflow.' `
            -Lines @('Planned: query NVIDIA releases, select version, install driver, optional strip components, import NVCP settings, MSI mode, vibrance, monitor speakers.', 'mpware does not download GPU drivers yet.')

        New-mpwareTweak `
            -Id 'restore-install-store' `
            -Name 'Restore: install Microsoft Store' `
            -Category 'Restore' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Matches mpware restore option for reinstalling Microsoft Store.' `
            -ApplySummary 'Runs wsreset -i.' `
            -UndoSummary 'No automatic undo; Store can be removed only through separate debloat workflows.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareCommandAction -TweakId 'restore-install-store' -Name 'Install Microsoft Store' -FilePath 'wsreset.exe' -ArgumentList @('-i') -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Undo Microsoft Store install' -Lines @('No automatic undo is provided for wsreset -i.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareLogOnlyTweak `
            -Id 'restore-mpware-bundle' `
            -Name 'Restore: mpware restore bundle' `
            -Category 'Restore' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for restore screen.' `
            -ApplySummary 'Documents restore options: enable updates, enable Defender, enable services, repair Xbox apps, disable QoS upload, unblock OEM downloads, unpause updates, restore default context menu, remove dark winver, and revert registry tweaks.' `
            -Lines @('mpware already has per-tweak undo for actions it applies.', 'Planned broad restore actions: enable updates, enable services, repair Xbox apps, disable QoS upload, unblock Razer/ASUS hosts, unpause updates, restore context menu, remove dark winver, revert imported registry tweaks.')

        New-mpwareTweak `
            -Id 'cleanup-temp-recycle' `
            -Name 'Ultimate Cleanup: temp files and recycle bin' `
            -Category 'Cleanup' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Implements the least risky part of mpware Ultimate Cleanup: temp folders and recycle bin.' `
            -ApplySummary 'Deletes files under user/system temp folders and empties the recycle bin.' `
            -UndoSummary 'Deleted temporary files cannot be automatically restored.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareTempCleanup -TweakId 'cleanup-temp-recycle' -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Undo temp cleanup' -Lines @('Deleted temporary files cannot be automatically restored.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareLogOnlyTweak `
            -Id 'cleanup-advanced-bundle' `
            -Name 'Ultimate Cleanup: advanced bundle' `
            -Category 'Cleanup' `
            -Risk 'High' `
            -RequiresAdmin $true `
            -Description 'Feature parity for the rest of mpware cleanup: event logs, Windows.old, duplicate drivers, cleanmgr, shader cache, update cleanup, and error reports.' `
            -ApplySummary 'Logs advanced cleanup areas for manual review.' `
            -Lines @('Covered upstream areas: event viewer logs, Windows logs, NVIDIA shader cache, Windows.old, duplicate drivers, disk cleanup on all drives, update cleanup, WER files, old chkdsk files, feedback hub archive, diagnostic viewer DB, device driver packages.', 'These are intentionally split from basic cleanup because some are destructive or slow.')

        New-mpwareTweak `
            -Id 'utility-repair-windows' `
            -Name 'Utility: repair Windows' `
            -Category 'Utilities' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Runs the same kind of repair utility exposed in mpware: DISM restore health followed by SFC.' `
            -ApplySummary 'Runs DISM and SFC repair commands.' `
            -UndoSummary 'No undo is needed; these are repair scans.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareRepairWindows -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Undo repair Windows' -Lines @('Repair scans do not need undo.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareTweak `
            -Id 'utility-restart-explorer' `
            -Name 'Utility: restart Explorer' `
            -Category 'Utilities' `
            -Risk 'Low' `
            -Description 'Restarts explorer.exe, matching the mpware utility action.' `
            -ApplySummary 'Stops and restarts explorer.exe.' `
            -UndoSummary 'No undo is needed.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareRestartExplorer -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareLogOnlyAction -Name 'Undo restart Explorer' -Lines @('No undo is needed.') -DryRun $DryRun -Log $Log
            } `
            -GetState {
                'Available'
            }

        New-mpwareLogOnlyTweak `
            -Id 'utility-restart-bios' `
            -Name 'Utility: restart to BIOS' `
            -Category 'Utilities' `
            -Risk 'High' `
            -RequiresAdmin $true `
            -Description 'mpware coverage for restart-to-BIOS action.' `
            -ApplySummary 'Logs the firmware reboot command instead of immediately restarting the PC from a batch action.' `
            -Lines @('Manual command: shutdown /r /fw /t 0', 'This should stay behind a confirmation dialog because it immediately reboots to firmware settings.')

        New-mpwareBlockedTweak `
            -Id 'activation-kms-blocked' `
            -Name 'Activate Windows' `
            -Category 'Blocked' `
            -Description 'mpware does not implement unauthorized activation tooling.' `
            -Reason 'Windows activation bypass/KMS tooling is not included. Use a valid Microsoft license or legitimate organization activation.'

        New-mpwareBlockedTweak `
            -Id 'install-remote-scripts-blocked' `
            -Name 'Install other remote scripts' `
            -Category 'Blocked' `
            -Description 'mpware does not auto-run mutable remote code.' `
            -Reason 'Running mutable remote scripts without pinning commits or checksums is unsafe. Add pinned sources and review prompts before enabling.'
    )
}

function Get-mpwareTweaks {
    $teamsPackages = @('MSTeams', 'MicrosoftTeams')
    $xboxServices = @('XblAuthManager', 'XblGameSave', 'XboxGipSvc', 'XboxNetApiSvc')
    $compatTasks = @(
        @{ TaskPath = '\Microsoft\Windows\Application Experience\'; TaskName = 'Microsoft Compatibility Appraiser' }
    )

    $baseTweaks = @(
        New-mpwareTweak `
            -Id 'perf-transparency' `
            -Name 'Disable transparency effects' `
            -Category 'Performance' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Turns off Windows transparency effects to reduce GPU/compositor work and create a cleaner flat UI.' `
            -ApplySummary 'Sets EnableTransparency to 0 for the current user.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'EnableTransparency'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'perf-transparency' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'perf-transparency' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Name = 'EnableTransparency'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'perf-visual-effects' `
            -Name 'Prefer best performance visual effects' `
            -Category 'Performance' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Asks Windows to prefer performance over animations and visual effects. You may need to sign out for every visual setting to refresh.' `
            -ApplySummary 'Sets VisualFXSetting to Best Performance and disables window minimize/maximize animation.' `
            -UndoSummary 'Restores the previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'; Name = 'VisualFXSetting'; Type = 'DWord'; Value = 3 },
                    @{ Path = 'HKCU:\Control Panel\Desktop\WindowMetrics'; Name = 'MinAnimate'; Type = 'String'; Value = '0' }
                )
                Invoke-mpwareRegistrySet -TweakId 'perf-visual-effects' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'perf-visual-effects' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'; Name = 'VisualFXSetting'; Value = 3 },
                    @{ Path = 'HKCU:\Control Panel\Desktop\WindowMetrics'; Name = 'MinAnimate'; Value = '0' }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'perf-game-dvr' `
            -Name 'Disable Game DVR capture' `
            -Category 'Performance' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Disables background Xbox Game Bar recording/capture settings that can cost resources during games.' `
            -ApplySummary 'Turns off AppCaptureEnabled and GameDVR_Enabled for the current user.' `
            -UndoSummary 'Restores the previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\System\GameConfigStore'; Name = 'GameDVR_Enabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR'; Name = 'AppCaptureEnabled'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'perf-game-dvr' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'perf-game-dvr' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\System\GameConfigStore'; Name = 'GameDVR_Enabled'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR'; Name = 'AppCaptureEnabled'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'perf-ultimate-power' `
            -Name 'Activate Ultimate Performance power plan' `
            -Category 'Performance' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Creates and activates the hidden Ultimate Performance power plan. Best for desktops; laptops may use more power and run hotter.' `
            -ApplySummary 'Duplicates the Microsoft Ultimate Performance scheme and activates it.' `
            -UndoSummary 'Restores the power plan that was active before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareUltimatePowerPlan -TweakId 'perf-ultimate-power' -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareUltimatePowerPlan -TweakId 'perf-ultimate-power' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareUltimatePowerPlan
            }

        New-mpwareTweak `
            -Id 'perf-power-throttling' `
            -Name 'Disable Windows power throttling' `
            -Category 'Performance' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Disables Windows power throttling policy. It can help latency-sensitive desktop workloads but may reduce battery life.' `
            -ApplySummary 'Sets HKLM PowerThrottlingOff to 1.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling'; Name = 'PowerThrottlingOff'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'perf-power-throttling' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'perf-power-throttling' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling'; Name = 'PowerThrottlingOff'; Value = 1 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'privacy-ad-id' `
            -Name 'Disable advertising ID' `
            -Category 'Privacy' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Stops apps from using the per-user Windows advertising identifier for personalized ads.' `
            -ApplySummary 'Sets AdvertisingInfo Enabled to 0 for the current user.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Enabled'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'privacy-ad-id' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'privacy-ad-id' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Enabled'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'privacy-tailored-experiences' `
            -Name 'Disable tailored experiences' `
            -Category 'Privacy' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Stops Windows from using diagnostic data to personalize tips, ads, and recommendations.' `
            -ApplySummary 'Sets TailoredExperiencesWithDiagnosticDataEnabled to 0 for the current user.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy'; Name = 'TailoredExperiencesWithDiagnosticDataEnabled'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'privacy-tailored-experiences' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'privacy-tailored-experiences' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy'; Name = 'TailoredExperiencesWithDiagnosticDataEnabled'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'privacy-diagnostics-basic' `
            -Name 'Limit diagnostic data policy' `
            -Category 'Privacy' `
            -Risk 'Low' `
            -RequiresAdmin $true `
            -Recommended $true `
            -Description 'Applies policy values that limit optional diagnostic data. Windows 11 Home/Pro may still enforce Microsoft minimums.' `
            -ApplySummary 'Sets AllowTelemetry to 1 and limits diagnostic log collection under HKLM policy.' `
            -UndoSummary 'Restores the previous policy values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Type = 'DWord'; Value = 1 },
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'LimitDiagnosticLogCollection'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'privacy-diagnostics-basic' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'privacy-diagnostics-basic' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Value = 1 },
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'LimitDiagnosticLogCollection'; Value = 1 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'privacy-compat-appraiser' `
            -Name 'Disable Compatibility Appraiser task' `
            -Category 'Privacy' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Disables Microsoft Compatibility Appraiser, a scheduled telemetry/compatibility scan task.' `
            -ApplySummary 'Disables the scheduled task under Microsoft Windows Application Experience.' `
            -UndoSummary 'Restores the task enabled state captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareTaskDisable -TweakId 'privacy-compat-appraiser' -Tasks $compatTasks -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareTaskDisable -TweakId 'privacy-compat-appraiser' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareTasksDisabled -Tasks $compatTasks
            }

        New-mpwareTweak `
            -Id 'debloat-consumer-content' `
            -Name 'Disable consumer content and app suggestions' `
            -Category 'Debloat' `
            -Risk 'Low' `
            -RequiresAdmin $true `
            -Recommended $true `
            -Description 'Reduces promoted apps, tips, suggested content, and silent consumer app installs.' `
            -ApplySummary 'Sets Windows CloudContent policy and current-user ContentDeliveryManager values.' `
            -UndoSummary 'Restores the previous registry values captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsConsumerFeatures'; Type = 'DWord'; Value = 1 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SilentInstalledAppsEnabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SystemPaneSuggestionsEnabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338388Enabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338389Enabled'; Type = 'DWord'; Value = 0 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353698Enabled'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'debloat-consumer-content' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'debloat-consumer-content' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsConsumerFeatures'; Value = 1 },
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SilentInstalledAppsEnabled'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'debloat-hide-widgets' `
            -Name 'Hide Widgets from taskbar' `
            -Category 'Debloat' `
            -Risk 'Low' `
            -Recommended $true `
            -Description 'Hides the Windows Widgets button from the taskbar for a cleaner desktop.' `
            -ApplySummary 'Sets TaskbarDa to 0 for the current user.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarDa'; Type = 'DWord'; Value = 0 }
                )
                Invoke-mpwareRegistrySet -TweakId 'debloat-hide-widgets' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'debloat-hide-widgets' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarDa'; Value = 0 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'debloat-disable-copilot' `
            -Name 'Disable Windows Copilot policy' `
            -Category 'Debloat' `
            -Risk 'Low' `
            -Description 'Disables Windows Copilot for the current user through policy. Availability depends on Windows build and region.' `
            -ApplySummary 'Sets TurnOffWindowsCopilot to 1 under HKCU policy.' `
            -UndoSummary 'Restores the previous registry value captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                $entries = @(
                    @{ Path = 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot'; Name = 'TurnOffWindowsCopilot'; Type = 'DWord'; Value = 1 }
                )
                Invoke-mpwareRegistrySet -TweakId 'debloat-disable-copilot' -Entries $entries -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareRegistrySet -TweakId 'debloat-disable-copilot' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                $entries = @(
                    @{ Path = 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot'; Name = 'TurnOffWindowsCopilot'; Value = 1 }
                )
                Test-mpwareRegistryEntriesApplied -Entries $entries
            }

        New-mpwareTweak `
            -Id 'debloat-remove-teams-personal' `
            -Name 'Remove Teams personal AppX' `
            -Category 'Debloat' `
            -Risk 'Medium' `
            -Description 'Removes Microsoft Teams AppX packages for the current user only. Undo tries to reinstall Teams with winget.' `
            -ApplySummary 'Runs Remove-AppxPackage for MSTeams/MicrosoftTeams packages installed for the current user.' `
            -UndoSummary 'Uses winget to reinstall Microsoft Teams if winget is available.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareAppxRemoval -TweakId 'debloat-remove-teams-personal' -PackageNames $teamsPackages -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareTeamsPersonal -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareAppxRemoved -PackageNames $teamsPackages
            }

        New-mpwareTweak `
            -Id 'services-xbox-manual' `
            -Name 'Set Xbox services to Manual' `
            -Category 'Services' `
            -Risk 'Medium' `
            -RequiresAdmin $true `
            -Description 'Sets common Xbox services to Manual so they do not run unless needed. Skip this if you use Game Pass, Xbox networking, or Xbox accessories often.' `
            -ApplySummary 'Changes Xbox service startup types to Manual and does not remove the services.' `
            -UndoSummary 'Restores each service startup type captured before applying.' `
            -Apply {
                param([bool]$DryRun, [scriptblock]$Log)
                Invoke-mpwareServiceStartup -TweakId 'services-xbox-manual' -Services $xboxServices -StartupType 'Manual' -StopRunning $false -DryRun $DryRun -Log $Log
            } `
            -Undo {
                param([bool]$DryRun, [scriptblock]$Log)
                Restore-mpwareServiceStartup -TweakId 'services-xbox-manual' -DryRun $DryRun -Log $Log
            } `
            -GetState {
                Test-mpwareServicesStartupType -Services $xboxServices -StartupType 'Manual'
            }
    )

    return @($baseTweaks + (Get-mpwareParityTweaks))
}





