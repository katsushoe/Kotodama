param(
    [Parameter(Mandatory)][string]$UserSid,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [switch]$RemoveMachineInstall
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Kotodama'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this initial migration with Windows administrator privileges.'
}
$sid = [Security.Principal.SecurityIdentifier]::new($UserSid)
$account = $sid.Translate([Security.Principal.NTAccount]).Value
if ($sid.Value -notlike 'S-1-5-21-*') { throw 'Specify the intended Windows user SID.' }
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
Start-Transcript -LiteralPath (Join-Path $BackupDirectory 'user-migration.log') -Append
try {
    $resolved = [IO.Path]::GetFullPath($root).TrimEnd('\')
    if ($resolved -ne 'C:\Kotodama') { throw 'Unexpected installation root.' }
    if (Test-Path -LiteralPath $root) {
        Get-Acl -LiteralPath $root | Export-Clixml -LiteralPath (Join-Path $BackupDirectory 'root-acl.xml')
        $config = Join-Path $root 'config\kotodama.json'
        if (Test-Path -LiteralPath $config) {
            Copy-Item -LiteralPath $config -Destination (Join-Path $BackupDirectory 'kotodama.json') -Force
        }
    }
    if ($RemoveMachineInstall) {
        $products = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -eq 'Kotodama' -and $_.Publisher -eq 'Akatsukisoft' })
        if ($products.Count -gt 1) { throw 'Multiple machine installations found; inspect before migration.' }
        foreach ($product in $products) {
            if ($product.PSChildName -notmatch '^\{[0-9A-Fa-f-]{36}\}$') { throw 'Unexpected MSI product identifier.' }
            $log = Join-Path $BackupDirectory 'machine-uninstall.log'
            $process = Start-Process msiexec.exe -ArgumentList @('/x', $product.PSChildName, '/qn', '/norestart', '/l*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -notin @(0, 3010)) { throw "Machine MSI uninstall failed: $($process.ExitCode)" }
        }
        if (Get-Service Kotodama -ErrorAction SilentlyContinue) { throw 'The old Windows service still exists.' }
    }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $acl = Get-Acl -LiteralPath $root
    $acl.SetAccessRuleProtection($true, $true)
    Set-Acl -LiteralPath $root -AclObject $acl
    $acl = Get-Acl -LiteralPath $root
    foreach ($rule in @($acl.Access)) {
        $ruleSid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        # Keep administrators/SYSTEM and read-only group entries; remove broad write access.
        if ($ruleSid -eq 'S-1-5-11' -or
            ($ruleSid -eq 'S-1-5-32-545' -and
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -ne 0)) {
            $acl.RemoveAccessRuleSpecific($rule)
        }
    }
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $root -AclObject $acl
    foreach ($name in @('bin', 'config', 'data', 'logs')) {
        $path = Join-Path $root $name
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        # Protected or explicitly restricted descendants also receive the intended user's access.
        & icacls.exe $path /grant ('*' + $UserSid + ':(OI)(CI)M') /T /Q
        if ($LASTEXITCODE -ne 0) { throw "ACL update failed: $name" }
    }
    Write-Output "Provisioned Modify access for $account ($UserSid) at $root."
}
finally {
    Stop-Transcript
}
