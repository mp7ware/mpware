# mpware package notes

This package contains the patched mpware PowerShell runtime and a lowercase `mpware.exe` launcher.

Included:
- PowerShell runtime, modules, registry tweak files, context-menu `.reg` files, restore tooling, driver/install helper scripts, and documentation.
- Required license notices in `THIRD_PARTY_NOTICES.md`.

Changed in the mpware runtime:
- Public branding and config names use lowercase `mpware`.
- Automatic self-update into a different release channel is disabled.
- Automatic Microsoft Defender exclusions are disabled.
- Windows activation/KMS tooling is blocked.
- The "Disable Defender" group-policy tweak and "Strip Windows Defender" helper are blocked.
- Placeholder black/modern icon assets are generated for the release package.
