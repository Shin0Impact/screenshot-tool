<#
  Installs the UIAccess build of the screenshot tool.

  Why: Windows does not let a normal program see keys typed into an elevated window (a game that
  was started as administrator, Task Manager, ...), so the hotkeys do nothing there. Running the app
  as administrator fixes that but needs elevation. A "UIAccess" app gets the same reach WITHOUT being
  elevated, but Windows only starts one if it is
    1. signed with a certificate the machine trusts, and
    2. installed in a protected folder such as Program Files.
  This script does both. It asks for administrator rights once, creates a local self-signed
  certificate that only trusts this one exe, installs to C:\Program Files\ScreenshotTool and sets the
  app to start at logon as a normal (non-elevated) program.

  Usage:   right-click > Run with PowerShell     (or: powershell -ExecutionPolicy Bypass -File .\Install-UIAccess.ps1)
  Remove:  .\Install-UIAccess.ps1 -Uninstall
  Put Screenshot-UIAccess*.exe in the same folder as this script, or pass -Exe <path>.
#>
param(
    [string]$Exe,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$self = $MyInvocation.MyCommand.Path
$here = Split-Path -Parent $self

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$self`"")
    if ($Exe) { $argList += @('-Exe', "`"$Exe`"") }
    if ($Uninstall) { $argList += '-Uninstall' }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    exit
}

$subject = 'CN=Screenshot Tool (local)'
$dir     = Join-Path $env:ProgramFiles 'ScreenshotTool'
$target  = Join-Path $dir 'Screenshot.exe'
$task    = 'ScreenshotTool'

try {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Screenshot*' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600

    if ($Uninstall) {
        Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
        foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher') {
            Get-ChildItem $store | Where-Object { $_.Subject -eq $subject } | Remove-Item -Force
        }
        Write-Host 'Removed.' -ForegroundColor Green
        return
    }

    if (-not $Exe) {
        $found = Get-ChildItem $here -Filter 'Screenshot-UIAccess*.exe' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { $Exe = $found.FullName }
    }
    if (-not $Exe -or -not (Test-Path $Exe)) {
        throw 'Could not find Screenshot-UIAccess*.exe next to this script (or pass -Exe <path>).'
    }

    # 1. a local code-signing certificate, trusted on this machine only
    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(10) `
            -KeyExportPolicy NonExportable
    }
    $cer = Join-Path $env:TEMP 'screenshot-tool-local.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    Remove-Item $cer -Force

    # 2. install to the protected folder and sign
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item $Exe $target -Force
    $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -HashAlgorithm SHA256
    if ($sig.Status -ne 'Valid') { throw "Signing failed: $($sig.Status) $($sig.StatusMessage)" }

    # 3. start at logon as a NORMAL (not elevated) program
    $user = "$env:USERDOMAIN\$env:USERNAME"
    Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
    Register-ScheduledTask -TaskName $task `
        -Action    (New-ScheduledTaskAction -Execute $target) `
        -Trigger   (New-ScheduledTaskTrigger -AtLogOn -User $user) `
        -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited) `
        -Settings  (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -ExecutionTimeLimit ([TimeSpan]::Zero)) | Out-Null
    Start-ScheduledTask -TaskName $task

    Write-Host ''
    Write-Host 'Installed and started.' -ForegroundColor Green
    Write-Host "  program : $target"
    Write-Host "  log     : $env:LOCALAPPDATA\ScreenshotTool\screenshot-tool.log"
    Write-Host '  The first log line should say: elevated = 0, uiAccess = 1'
}
catch {
    Write-Host ''
    Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host 'Press Enter to close'
