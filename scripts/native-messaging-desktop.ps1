param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference = 'Stop'
$request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
try {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class NativeMessagingPhysicalFile {
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
    function Assert-PhysicalFile([string]$Path) {
        $physical = [NativeMessagingPhysicalFile]::Resolve($Path)
        if (-not $physical.Equals([IO.Path]::GetFullPath($Path), [StringComparison]::OrdinalIgnoreCase)) {
            throw "File was redirected: $Path -> $physical. Run registration from a normal Windows PowerShell window."
        }
    }
    $localRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ($localRoot -match '\\Packages\\.*\\LocalCache\\') { throw 'Desktop process still has a redirected LocalAppData location.' }
    $directory = Join-Path $localRoot 'AIScreenShot\native-messaging'
    $manifestPath = Join-Path $directory ($request.Browser + '.json')
    $vendor = if ($request.Browser -eq 'Chrome') { 'Google\Chrome' } elseif ($request.Browser -eq 'Edge') { 'Microsoft\Edge' } else { throw 'Invalid browser.' }
    if ($request.ExtensionId -notmatch '^[a-p]{32}$') { throw 'Invalid extension ID.' }
    $registryPath = "Software\$vendor\NativeMessagingHosts\com.aiscreenshot.bridge"
    if ($request.Mode -eq 'Install') {
        $sourceExe = [IO.Path]::GetFullPath([string]$request.HostPath)
        Assert-PhysicalFile $sourceExe
        if ($sourceExe -match '\\Packages\\.*\\LocalCache\\') { throw 'Native Host source must be a published directory outside a package LocalCache.' }
        $sourceDirectory = Split-Path $sourceExe -Parent
        foreach ($required in @('ScreenShot.NativeHost.dll','ScreenShot.NativeHost.deps.json','ScreenShot.NativeHost.runtimeconfig.json','ScreenShot.Core.dll')) {
            Assert-PhysicalFile (Join-Path $sourceDirectory $required)
        }
        # Fingerprint the full published runtime; upgrades never overwrite a running Host.
        $files = @(Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse -Force | Sort-Object FullName)
        $fingerprint = ($files | ForEach-Object { $_.FullName.Substring($sourceDirectory.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n"
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $version = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprint)))).Replace('-','').ToLowerInvariant().Substring(0,16) } finally { $sha.Dispose() }
        $hostDirectory = Join-Path $directory ('host\' + $version)
        New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($sourceDirectory.Length).TrimStart('\')
            $target = Join-Path $hostDirectory $relative
            if (-not (Test-Path -LiteralPath $target)) {
                New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
            Assert-PhysicalFile $target
            if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Deployed dependency hash mismatch: $relative" }
        }
        $hostExe = Join-Path $hostDirectory 'ScreenShot.NativeHost.exe'
        Assert-PhysicalFile $hostExe
        $manifest = @{name='com.aiscreenshot.bridge';description='AI ScreenShot browser bridge';path=$hostExe;type='stdio';allowed_origins=@("chrome-extension://$($request.ExtensionId)/")} | ConvertTo-Json
        [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
        Assert-PhysicalFile $manifestPath
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
            try { $key = $base.CreateSubKey($registryPath); try { $key.SetValue('', $manifestPath, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() } } finally { $base.Dispose() }
        }
    } elseif ($request.Mode -ne 'Verify') { throw 'Invalid registration operation.' }
    Assert-PhysicalFile $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.name -ne 'com.aiscreenshot.bridge' -or $manifest.type -ne 'stdio') { throw 'Invalid Native Messaging manifest name or type.' }
    if (@($manifest.allowed_origins).Count -ne 1 -or $manifest.allowed_origins[0] -ne "chrome-extension://$($request.ExtensionId)/") { throw 'Extension origin mismatch.' }
    $hostExe = [IO.Path]::GetFullPath([string]$manifest.path)
    if (-not $hostExe.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase) -or $hostExe -match '\\Packages\\.*\\LocalCache\\') { throw 'Host path is outside the real deployment directory.' }
    Assert-PhysicalFile $hostExe
    foreach ($required in @('ScreenShot.NativeHost.dll','ScreenShot.NativeHost.deps.json','ScreenShot.NativeHost.runtimeconfig.json','ScreenShot.Core.dll')) { Assert-PhysicalFile (Join-Path (Split-Path $hostExe -Parent) $required) }
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        try {
            $key = $base.OpenSubKey($registryPath)
            if (-not $key) { throw "Registration missing in $view." }
            try { if ($key.GetValue('') -ne $manifestPath -or $key.GetValueKind('') -ne [Microsoft.Win32.RegistryValueKind]::String) { throw "Registration mismatch in $view." } } finally { $key.Dispose() }
        } finally { $base.Dispose() }
    }
    $runtimeFiles = @(Get-ChildItem -LiteralPath (Split-Path $hostExe -Parent) -File -Recurse -Force)
    foreach ($file in $runtimeFiles) { Assert-PhysicalFile $file.FullName }
    $result = @{Success=$true;HostName=$manifest.name;ManifestPath=$manifestPath;ManifestExists=(Test-Path -LiteralPath $manifestPath -PathType Leaf);HostPath=$hostExe;HostExists=(Test-Path -LiteralPath $hostExe -PathType Leaf);RuntimeFileCount=$runtimeFiles.Count;RegistryPath="HKCU\$registryPath";RegistryValue=$manifestPath;ExtensionId=$request.ExtensionId;PhysicalFilesVerified=$true}
} catch { $result = @{Success=$false;Error=$_.Exception.Message} }
[IO.File]::WriteAllText([string]$request.ResultPath + '.tmp', ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[IO.File]::Move([string]$request.ResultPath + '.tmp', [string]$request.ResultPath)
if (-not $result.Success) { exit 1 }
