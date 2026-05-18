# ZOICWARE-Inspired Parity Map

This project aims for workflow parity with ZOICWARE while keeping a distinct UI, name, layout, and safety model.

## Implemented Now

- Registry-style Windows 11 quality-of-life tweaks
- Group-policy style update, privacy, telemetry, and debloat tweaks
- Scheduled task disable with restore state
- ZOICWARE-style service startup cleanup with restore state
- Current-user AppX removal for selected bloat
- ZOICWARE debloat presets: all, keep Store/Xbox/Edge, keep Store/Xbox, keep Edge, keep Store
- Optional theme/update/shutdown tweaks
- Context menu feature groups represented as guided actions
- Power-plan activation with previous plan restore
- Windows 11 shell tweak groups represented as guided actions
- winget-based package/browser installer entries
- Network and NVIDIA driver helper entries represented as guided actions
- Restore, cleanup, repair, restart Explorer, and restart-to-BIOS utility entries
- Import/export tweak selections as JSON profiles
- Per-tweak explanations through tooltips
- Preview log before applying real changes
- Undo/restore actions where state can be captured

## Planned Safe Parity

- Custom AppX package picker
- Individual reversible context menu actions
- Individual reversible Windows 11 shell and asset actions with checksummed assets
- Advanced cleanup tools for event logs, Windows.old, shader cache, duplicate drivers, and cleanmgr profiles
- Power-plan import/remove UI with bundled `.pow` files
- Driver helper pages that link to vendor installers instead of bundling drivers

## Explicitly Not 1:1

Some ZOICWARE feature groups are intentionally not copied as one-click actions:

- Disabling Microsoft Defender
- Fully disabling Windows Update
- Windows activation tooling
- Removing broad sets of scheduled tasks without a narrow allowlist
- Disabling large batches of services without explaining each service and offering restore data

Those actions can reduce security or break normal Windows behavior. If added later, they should be guarded behind warnings, preview output, restore data, and separate opt-in profiles.

## Attribution

ZOICWARE is MIT licensed. Feature inspiration and any future reused source should preserve the upstream copyright notice in `THIRD_PARTY_NOTICES.md`.


