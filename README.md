# mpware

mpware is a Windows 11 optimization and debloat package with a portable dashboard-style `mpware.exe` launcher.

The current release package is:

- `dist/mpware/mpware.exe`
- `dist/mpware.zip`

`mpware.exe` embeds the PowerShell runtime and can extract it into the current user's local app data folder when the sidecar runtime folder is not beside it.

The launcher requests Administrator on start. Registry Tweaks create a Windows System Restore point before import, then apply selected registry groups, run required follow-up actions such as solid-black wallpaper refresh, Ultimate Performance activation, or the hidden `SetTimerResolution.exe` boot task, and restart Explorer.

PowerShell helper windows close automatically after successful actions. They stay open only when an error needs to be read.

Restore Changes currently exposes rollback for the registry tweak bundle only. Debloat and cleanup actions do not auto-create restore points, and removed apps may need to be reinstalled from Microsoft Store or winget.

The context-menu bundle is intentionally limited to shutdown/restart, run-as-admin, and kill non-responding tasks.

