$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
& $dotnet build (Join-Path $root 'src\ScreenShot.App\ScreenShot.App.csproj') -c Release
if ($LASTEXITCODE) { throw 'App build failed' }
& $dotnet build (Join-Path $root 'src\ScreenShot.NativeHost\ScreenShot.NativeHost.csproj') -c Release
if ($LASTEXITCODE) { throw 'Native Host build failed' }
& $dotnet run --project (Join-Path $root 'tests\ScreenShot.Tests\ScreenShot.Tests.csproj') -c Release
if ($LASTEXITCODE) { throw 'Core tests failed' }
Push-Location (Join-Path $root 'extension')
try { node --test *.test.js; if ($LASTEXITCODE) { throw 'Extension tests failed' }; foreach ($source in @('background.js','connection.js','popup.js')) { node --check $source; if ($LASTEXITCODE) { throw "Extension syntax failed: $source" } } } finally { Pop-Location }
