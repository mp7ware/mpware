If (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]'Administrator')) {
    Start-Process PowerShell.exe -ArgumentList ("-NoProfile -ExecutionPolicy Bypass -File `"{0}`"" -f $PSCommandPath) -Verb RunAs
    Exit
}

$Host.UI.RawUI.WindowTitle = 'mpware GPU driver'
$Host.UI.RawUI.BackgroundColor = 'Black'
$Host.PrivateData.ProgressBackgroundColor = 'Black'
$Host.PrivateData.ProgressForegroundColor = 'White'
$ProgressPreference = 'SilentlyContinue'
Clear-Host

if (Get-Command Disable-MpwareConsoleQuickEdit -ErrorAction SilentlyContinue) {
    Disable-MpwareConsoleQuickEdit
}

function Write-MpwareHeader {
    param([string]$Title, [string]$Subtitle)
    Clear-Host
    Write-Host 'MPWARE' -ForegroundColor Cyan
    Write-Host $Title -ForegroundColor White
    if ($Subtitle) {
        Write-Host $Subtitle -ForegroundColor DarkGray
    }
    Write-Host ''
}

function Write-MpwareOption {
    param([string]$Number, [string]$Label, [ConsoleColor]$Color = [ConsoleColor]::White)
    Write-Host (" {0}.  {1}" -f $Number, $Label) -ForegroundColor $Color
}

function Stop-MpwareWithError {
    param([string]$Message)
    Write-Host ''
    Write-Host $Message -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
    exit 1
}

if ((Get-Command Check-Internet -ErrorAction SilentlyContinue) -and -not (Check-Internet)) {
    Stop-MpwareWithError 'Internet connection is required.'
}
elseif (-not (Get-Command Check-Internet -ErrorAction SilentlyContinue) -and -not (Test-Connection -ComputerName '8.8.8.8' -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
    Stop-MpwareWithError 'Internet connection is required.'
}

Write-MpwareHeader -Title 'Install GPU Drivers' -Subtitle 'Choose your GPU vendor and continue with the official driver installer.'
Write-MpwareOption -Number '1' -Label 'NVIDIA' -Color Green
Write-MpwareOption -Number '2' -Label 'AMD' -Color Red
Write-MpwareOption -Number '3' -Label 'INTEL' -Color Blue
Write-Host ''

while ($true) {
    $choice = Read-Host 'Select GPU'
    if ($choice -notmatch '^[1-3]$') {
        Write-Host 'Invalid input. Please select 1, 2, or 3.' -ForegroundColor Yellow
        continue
    }

    switch ($choice) {
        '1' {
            Write-MpwareHeader -Title 'NVIDIA Driver' -Subtitle 'Latest Game Ready DCH WHQL package'
            Write-Host 'Unless you need recording or replay features,' -ForegroundColor White
            Write-Host 'avoid installing the NVIDIA App.' -ForegroundColor White
            Write-Host ''
            Write-Host 'Game Filter (ALT+F3) and Statistics (ALT+R)' -ForegroundColor White
            Write-Host 'can reduce FPS when enabled.' -ForegroundColor White
            Write-Host ''
            Write-Host "In the NVIDIA App, turn off 'Automatically optimize newly added games and apps'." -ForegroundColor DarkGray
            Write-Host ''

            $uri = 'https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=120&pfid=929&osID=57&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1'
            $response = Invoke-WebRequest -Uri $uri -Method GET -UseBasicParsing
            $payload = $response.Content | ConvertFrom-Json
            $version = $payload.IDS[0].downloadInfo.Version
            $windowsVersion = if ([Environment]::OSVersion.Version -ge (New-Object Version 9, 1)) { 'win10-win11' } else { 'win8-win7' }
            $windowsArchitecture = if ([Environment]::Is64BitOperatingSystem) { '64bit' } else { '32bit' }
            $url = "https://international.download.nvidia.com/Windows/$version/$version-desktop-$windowsVersion-$windowsArchitecture-international-dch-whql.exe"
            $target = Join-Path $env:SystemRoot 'Temp\nvidiadriver.exe'

            Write-Host "Downloading NVIDIA Driver $version..." -ForegroundColor Cyan
            Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing -ErrorAction Stop
            Write-Host 'Launching NVIDIA driver installer...' -ForegroundColor Green
            Start-Process $target
            exit
        }
        '2' {
            Write-MpwareHeader -Title 'AMD Driver' -Subtitle 'AMD web installer'
            Write-Host 'Downloading AMD Driver Web Installer...' -ForegroundColor Cyan

            $downloadAmd = Invoke-WebRequest 'https://www.amd.com/en/support/download/drivers.html' -UseBasicParsing |
                Select-Object -ExpandProperty Links |
                Where-Object { $_.href -match 'drivers\.amd\.com/drivers/installer/.*/whql/amd-software-adrenalin-edition-.*-minimalsetup-.*_web\.exe' } |
                Select-Object -First 1 -ExpandProperty href

            $spoofWebBrowser = @{
                'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36'
                'Accept'     = 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8'
                'Referer'    = 'https://www.amd.com/'
            }

            $target = Join-Path $env:SystemRoot 'Temp\amddriver.exe'
            Invoke-WebRequest -Uri $downloadAmd -UseBasicParsing -Headers $spoofWebBrowser -OutFile $target -ErrorAction Stop | Out-Null
            Write-Host 'Launching AMD driver installer...' -ForegroundColor Green
            Start-Process $target
            exit
        }
        '3' {
            Write-MpwareHeader -Title 'Intel Driver' -Subtitle 'Opening the official Intel graphics driver page'
            Start-Process 'https://www.intel.com/content/www/us/en/search.html#sortCriteria=%40lastmodifieddt%20descending&f-operatingsystem_en=Windows%2011%20Family*&f-downloadtype=Drivers&cf-tabfilter=Downloads&cf-downloadsppth=Graphics'
            exit
        }
    }
}
