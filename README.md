# mpware

mpware is a Windows 11 optimization and debloat package with a portable `mpware.exe` release folder.

The current release package is:

- `dist/mpware/mpware.exe`
- `dist/mpware/_FOLDERMUSTBEONCDRIVE/`
- `dist/mpware.zip`

Keep the folder structure together. `mpware.exe` is a launcher for the patched PowerShell runtime beside it.

## What Is Included

- Patched PowerShell runtime for Windows 11 optimization
- Registry tweak file, context-menu `.reg` files, restore scripts, driver helpers, cleanup tools, and utilities
- Generated black/modern icon assets
- A separate safer WPF prototype in `src/`


Those changes are documented in `dist/mpware/NOTICE.md`.

## Run

Extract `dist/mpware.zip`, then run:

```powershell
.\mpware.exe
```

Use **Run as administrator** for tweaks that need elevated permissions.

## Build

Refresh the packaged release and zip:

```powershell
.\build\Rebuild-Release.ps1
```

The WPF prototype can be built with:

```powershell
.\build\Build-Exe.ps1
```

## Feature Map

See `docs/MPWARE_FEATURES.md`.

## Notices

See `THIRD_PARTY_NOTICES.md` for required license notices.

