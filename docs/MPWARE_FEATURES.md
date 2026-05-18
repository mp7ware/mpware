# mpware Feature Map

mpware groups Windows 11 optimization features into clear areas while keeping risky actions visible and blocked where appropriate.

## Included Areas

- Registry performance and usability tweaks
- Group Policy update and telemetry controls
- Scheduled task cleanup
- Service startup cleanup with restore state
- Debloat presets: all, keep Store/Xbox/Edge, keep Store/Xbox, keep Edge, keep Store
- Custom debloat planning
- Optional desktop, shell, update, input, boot, and time-service tweaks
- Context menu add/remove actions
- Power plan import and hidden plan controls
- Windows 11 shell restoration actions
- Runtime/package install helpers
- Browser, driver, and utility launchers
- Restore tools
- Cleanup tools
- Repair and restart utilities

## Blocked Areas

These actions are intentionally blocked in the runtime:

- Unauthorized activation tooling
- One-click endpoint protection disable or strip actions
- Mutable remote script execution without review

## Current Implementation

The WPF prototype keeps a reversible tweak catalog in `src/mpware.tweaks.ps1`.

The packaged runtime lives in `dist/mpware` and is launched through `dist/mpware/mpware.exe`.

