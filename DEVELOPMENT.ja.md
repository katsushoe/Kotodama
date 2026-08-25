# 開発と検証

## 環境

- C# / .NET 10
- SQLite / `Microsoft.Data.Sqlite`
- xUnit / FluentAssertions
- WiX Toolset 5

## ビルドとテスト

```powershell
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
dotnet test Kotodama.slnx -c Release --no-restore
dotnet format Kotodama.slnx --verify-no-changes --no-restore
```

テストには規則検証、Open World、競合Claim、対称Relation、時間境界、Source、Event、dreamの各格納方式、実並行処理、障害注入、MCP stdio／Streamable HTTP結合を含みます。

## MSI生成

```powershell
dotnet publish src/Kotodama/Kotodama.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/win-x64

$publishDir = (Resolve-Path artifacts/publish/win-x64).Path
.tools/wix build installer/Package.wxs `
  -arch x64 `
  -d ProductVersion=0.3.1 `
  -d PublishDir=$publishDir `
  -o artifacts/release/Kotodama-0.3.1-x64.msi
```

## リリース完了条件

- warning 0、error 0
- 全テスト合格
- format検証合格
- x64 MSI生成
- Install、Upgrade、Uninstall実機確認
- Version表示確認
- MSI SHA-256作成・照合
- インストール済み実行ファイルとのMCP stdio通信確認
- 既定DBが`C:\Kotodama\data`へ作成されること
- 利用者データがUpgrade／Uninstallで不用意に削除されないこと
