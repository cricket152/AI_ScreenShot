param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist\AIScreenShot-win-x64' }
foreach ($retiredPath in @('extension','native-host','scripts\register-extension.ps1','scripts\unregister-extension.ps1','scripts\native-messaging-desktop.ps1','licenses\ChromeGPT.txt')) {
    if (Test-Path -LiteralPath (Join-Path $output $retiredPath)) { throw 'Output contains retired browser bridge files. Choose a fresh -OutputDirectory to avoid shipping obsolete features.' }
}
& $dotnet publish (Join-Path $root 'src\ScreenShot.App\ScreenShot.App.csproj') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE) { throw 'App publish failed' }
Copy-Item -LiteralPath (Join-Path $root 'scripts') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $output -Recurse -Force
Write-Output "Published: $output"
