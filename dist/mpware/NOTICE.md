# mpware package notes

This package contains the patched mpware PowerShell runtime and a lowercase portable `mpware.exe` launcher.

Included:
- PowerShell runtime, modules, registry tweak files, context-menu `.reg` files, restore tooling, driver/install helper scripts, and documentation.
- Required license notices in `THIRD_PARTY_NOTICES.md`.

Changed in the mpware runtime:
- Public branding and config names use lowercase `mpware`.
- The launcher can embed and extract its runtime for easier single-exe startup.
- Automatic self-update into a different release channel is disabled.
- Activation helpers, Defender-disable helpers, and PBO helper UI were removed from mpware.
- Placeholder black/modern icon assets are generated for the release package.
