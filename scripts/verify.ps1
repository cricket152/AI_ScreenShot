$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
& $dotnet build (Join-Path $root 'src\ScreenShot.App\ScreenShot.App.csproj') -c Release
if ($LASTEXITCODE) { throw 'App build failed' }
& $dotnet run --project (Join-Path $root 'tests\ScreenShot.Tests\ScreenShot.Tests.csproj') -c Release
if ($LASTEXITCODE) { throw 'Core tests failed' }
