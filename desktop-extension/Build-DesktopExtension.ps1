[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/Kotodama/Kotodama.csproj"
$manifestPath = Join-Path $PSScriptRoot "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("kotodama-dxt-" + [Guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $temporaryRoot "server"
$archivePath = Join-Path $resolvedOutput ("Kotodama-{0}-{1}.zip" -f $version, $Runtime)
$extensionPath = Join-Path $resolvedOutput ("Kotodama-{0}-{1}.dxt" -f $version, $Runtime)

try {
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

    dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $temporaryRoot "manifest.json")
    Compress-Archive -LiteralPath (Join-Path $temporaryRoot "manifest.json"), $publishDirectory -DestinationPath $archivePath -Force
    Move-Item -LiteralPath $archivePath -Destination $extensionPath -Force
    Write-Output $extensionPath
}
finally {
    $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemporary.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporary)) {
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
