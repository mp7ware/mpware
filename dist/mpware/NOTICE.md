# mpware upstream package

This package vendors the ZOICWARE source from https://github.com/zoicware/ZOICWARE under the MIT license and builds a lowercase `mpware.exe` launcher around a patched runtime copy.

Included:
- Patched ZOICWARE PowerShell source, modules, registry tweak files, context-menu `.reg` files, restore tooling, driver/install helper scripts, and documentation.
- `third_party/ZOICWARE-attribution`, upstream license and documentation files.

Changed in the mpware runtime:
- Public branding and config names changed to lowercase `mpware`.
- Automatic self-update to upstream ZOICWARE is disabled.
- Automatic Microsoft Defender exclusions are disabled.
- Windows activation/KMS tooling is blocked.
- The "Disable Defender" group-policy tweak and "Strip Windows Defender" helper are blocked.
- Placeholder black/modern icon assets are generated because the GitHub source archive does not include the release icon folder.
