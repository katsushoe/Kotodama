# Installer

Build the self-contained Windows x64 tray application and per-user MSI with WiX Toolset 5:

```powershell
dotnet publish src/Kotodama.Tray/Kotodama.Tray.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/user-win-x64
$publishDir = (Resolve-Path artifacts/publish/user-win-x64).Path
.tools/wix build installer/Package.wxs -arch x64 -d ProductVersion=0.21.0 -d PublishDir=$publishDir -o artifacts/release/Kotodama-0.21.0-user-x64.msi
```

The per-user MSI installs to `C:\Kotodama`, registers the tray application in HKCU Run,
and updates the user's PATH and AI client registrations. It does not register a Windows service.
The canonical `config\kotodama.json` is created only when absent and preserved during upgrades and uninstall.
Initial folder permission provisioning and migration from the legacy per-machine MSI require
`Initialize-UserInstall.ps1` with administrator privileges. Later MSI updates run without elevation.
See [installation](../INSTALLATION.ja.md) and [ADR-0002](../docs/decisions/ADR-0002-user-tray.ja.md).
