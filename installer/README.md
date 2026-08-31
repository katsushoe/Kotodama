# Installer

Build the Windows x64 MSI with WiX Toolset 5:

```powershell
dotnet publish src/Kotodama/Kotodama.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
$publishDir = (Resolve-Path artifacts/publish/win-x64).Path
.tools/wix build installer/Package.wxs -arch x64 -d ProductVersion=0.11.2 -d PublishDir=$publishDir -o artifacts/release/Kotodama-0.11.2-x64.msi
```

The MSI uses a stable upgrade code and installs per-machine to `C:\Kotodama`.
