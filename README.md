# mpware

mpware is a lowercase Windows 11 optimization/debloat package built from the ZOICWARE PowerShell source, rebranded and patched into a portable `mpware.exe` release folder.

The current release package is:

- `dist/mpware/mpware.exe`
- `dist/mpware/_FOLDERMUSTBEONCDRIVE/`
- `dist/mpware.zip`

Keep the folder structure together. `mpware.exe` is a launcher for the patched PowerShell runtime beside it.

## What Is Included

- ZOICWARE PowerShell source converted into a patched `mpware` runtime
- Registry tweak file, context-menu `.reg` files, restore scripts, driver helpers, cleanup tools, and utilities
- Generated black/modern placeholder icons because the GitHub source archive does not include ZOICWARE's release icon folder
- Upstream license/docs in `dist/mpware/third_party/ZOICWARE-attribution`
- A separate safer WPF prototype in `src/`

## Safety Changes

The runtime intentionally blocks or disables:

- Windows activation/KMS automation
- One-click Microsoft Defender disabling/stripping
- Automatic Microsoft Defender exclusions
- Upstream self-update into ZOICWARE releases

Those changes are documented in `dist/mpware/NOTICE.md`.

## Run

Extract `dist/mpware.zip`, then run:

```powershell
.\mpware.exe
```

Use **Run as administrator** for tweaks that need elevated permissions.

## Build

The upstream-package build script downloads ZOICWARE source into the ignored `vendor/` cache if it is missing, patches it, validates PowerShell syntax, generates placeholder assets, and builds `dist/mpware/mpware.exe` with PS2EXE.

```powershell
.\build\Build-UpstreamMpware.ps1
Compress-Archive -Path .\dist\mpware\* -DestinationPath .\dist\mpware.zip -Force
```

The original safer WPF prototype can be built with:

```powershell
.\build\Build-Exe.ps1
```

## Attribution

This project vendors and modifies ZOICWARE under its MIT license:

- https://github.com/zoicware/ZOICWARE
- https://github.com/zoicware/ZOICWARE/blob/main/features.md

See `THIRD_PARTY_NOTICES.md` and `dist/mpware/third_party/ZOICWARE-attribution/LICENSE`.
