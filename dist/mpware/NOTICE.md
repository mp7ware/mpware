# mpware package notes

This package contains the patched mpware PowerShell runtime and a lowercase portable `mpware.exe` launcher.

Included:
- PowerShell runtime, modules, registry tweak files, limited context-menu `.reg` files, registry restore tooling, driver/install helper scripts, timer-resolution helper, and documentation.
- Required license notices in `THIRD_PARTY_NOTICES.md`.

Changed in the mpware runtime:
- Public branding and config names use lowercase `mpware`.
- The launcher can embed and extract its runtime for easier single-exe startup.
- The launcher requests Administrator on start.
- Registry Tweaks create a Windows System Restore point before import; debloat, cleanup, NVIDIA, and restore tools do not auto-create restore points.
- PowerShell helper windows close automatically on success and stay open only when an error needs review.
- Restore Changes is limited to registry tweak rollback.
- Context-menu files are limited to shutdown/restart, run-as-admin, and kill non-responding tasks.
- Managed registry-list actions now handle solid-black wallpaper refresh, Ultimate Performance activation, and 0.5ms timer-resolution setup.
- Automatic self-update into a different release channel is disabled.
- Activation helpers, Defender-disable helpers, and PBO helper UI were removed from mpware.
