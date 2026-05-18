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
