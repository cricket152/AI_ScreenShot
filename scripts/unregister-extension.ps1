param([ValidateSet('Edge','Chrome')][string]$Browser='Edge')
$ErrorActionPreference='Stop'
$vendor=if($Browser -eq 'Edge'){'Microsoft\Edge'}else{'Google\Chrome'}
$key="HKCU:\Software\$vendor\NativeMessagingHosts\com.aiscreenshot.bridge"
if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key}
Write-Output "Unregistered AI ScreenShot bridge for $Browser. Extension removal is done in your browser."
