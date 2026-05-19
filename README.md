# mpware

mpware is a Windows 11 optimization and debloat package with a portable dashboard-style `mpware.exe` launcher.

The current release package is:

- `dist/mpware/mpware.exe`
- `dist/mpware.zip`

`mpware.exe` embeds the PowerShell runtime and can extract it into the current user's local app data folder when the sidecar runtime folder is not beside it.

## What Is Included

- Patched portable PowerShell runtime for Windows 11 optimization
- Native dark dashboard launcher with system cards, category pages, quick actions, and an activity log
- Registry tweak file, context-menu `.reg` files, restore scripts, driver helpers, cleanup tools, and utilities
- Generated black/modern icon assets

Those changes are documented in `dist/mpware/NOTICE.md`.

## Run

Run the exe directly, or extract `dist/mpware.zip` and run:

```powershell
.\mpware.exe
```

Use **Run as administrator** for tweaks that need elevated permissions.

## Build

Refresh the packaged exe and zip:

```powershell
.\build\Rebuild-Release.ps1
```

Smoke-test the embedded runtime:

```powershell
.\dist\mpware\mpware.exe --self-test
```

## Notices

See `THIRD_PARTY_NOTICES.md` for required license notices.

