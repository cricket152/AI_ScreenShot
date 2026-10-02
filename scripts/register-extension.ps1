[CmdletBinding(SupportsShouldProcess=$true)]
param([Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$ExtensionId,[ValidateSet('Edge','Chrome')][string]$Browser='Edge',[string]$HostPath,[switch]$VerifyOnly)
$ErrorActionPreference='Stop'
if (-not $VerifyOnly -and -not $HostPath) {
    $root = Split-Path $PSScriptRoot -Parent
    $candidates = @(
        (Join-Path $root 'native-host\ScreenShot.NativeHost.exe')
        (Join-Path $root 'dist\AIScreenShot-win-x64-final\native-host\ScreenShot.NativeHost.exe')
        (Join-Path $root 'dist\AIScreenShot-win-x64\native-host\ScreenShot.NativeHost.exe')
    )
    $HostPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $HostPath) {
        throw 'Native Host was not found. From the source directory, run scripts\publish.ps1 first; for a custom publish directory, pass -HostPath <full path to ScreenShot.NativeHost.exe>.'
    }
}
if (-not $VerifyOnly -and -not (Test-Path -LiteralPath $HostPath -PathType Leaf)) {
    throw "Native Host does not exist: $HostPath. Pass -HostPath with the published ScreenShot.NativeHost.exe path."
}
$resolvedHost = if ($VerifyOnly) { $null } else { (Resolve-Path -LiteralPath $HostPath).Path }
if (-not $VerifyOnly) {
    Write-Output "Native Host source: $resolvedHost"
    if (-not $PSCmdlet.ShouldProcess("Current-user $Browser Native Messaging deployment and registration", 'Deploy published Host to real LocalAppData and register AI ScreenShot bridge')) { return }
}
# A path seen from an MSIX process may be a merged view. The desktop broker performs
# deployment and verifies physical file handles before publishing the registry value.
if (-not ('NativeMessagingRequestFile' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class NativeMessagingRequestFile {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    public static string Resolve(string path) {
        using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            var result = new StringBuilder(32768);
            if (GetFinalPathNameByHandle(file.SafeFileHandle, result, (uint)result.Capacity, 0) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            string value = result.ToString();
            return value.StartsWith(@"\\?\UNC\") ? @"\\" + value.Substring(8) : value.StartsWith(@"\\?\") ? value.Substring(4) : value;
        }
    }
}
'@
}
$temporaryRequest = Join-Path ([IO.Path]::GetTempPath()) ('AIScreenShot-registration-' + [Guid]::NewGuid().ToString('N') + '.json')
[IO.File]::WriteAllText($temporaryRequest, '{}')
$requestPath = [NativeMessagingRequestFile]::Resolve($temporaryRequest)
$resultPath = $requestPath + '.result.json'
$request = @{Mode=if($VerifyOnly){'Verify'}else{'Install'};HostPath=$resolvedHost;Browser=$Browser;ExtensionId=$ExtensionId;ResultPath=$resultPath}
[IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$worker = Join-Path $PSScriptRoot 'native-messaging-desktop.ps1'
$command = "& '" + $worker.Replace("'", "''") + "' -RequestPath '" + $requestPath.Replace("'", "''") + "'"
$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
try {
    $shell = New-Object -ComObject Shell.Application
    $windows = $shell.Windows()
    $desktopHandle = 0
    $desktop = $windows.FindWindowSW(0, 0, 8, [ref]$desktopHandle, 1)
    if (-not $desktop) { throw 'Explorer desktop is unavailable.' }
    $desktop.Document.Application.ShellExecute($powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand", $PSScriptRoot, 'open', 0)
} catch { throw "Unable to start desktop registration. Run this script from a normal Windows PowerShell window. $($_.Exception.Message)" }
$deadline = [DateTime]::UtcNow.AddSeconds(60)
while (-not (Test-Path -LiteralPath $resultPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Desktop registration timed out; no success was confirmed. Run this script from a normal Windows PowerShell window.' }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (-not $result.Success) { throw $result.Error }
Write-Output ($result | ConvertTo-Json)
Write-Output "Physical deployment and current-user $Browser registration verified. Start AI ScreenShot and reload the extension; it will connect automatically."
