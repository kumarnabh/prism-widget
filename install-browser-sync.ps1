$ErrorActionPreference='Stop'
$prismRoot=$PSScriptRoot
$manifest=Get-Content -Raw -LiteralPath (Join-Path $prismRoot 'browser-extension\manifest.json') | ConvertFrom-Json
$hash=[Security.Cryptography.SHA256]::Create().ComputeHash([Convert]::FromBase64String($manifest.key))
$hex=-join ($hash[0..15] | ForEach-Object { $_.ToString('x2') })
$extensionId=-join ($hex.ToCharArray() | ForEach-Object { [char](97+[Convert]::ToInt32([string]$_,16)) })
$nativePath=Join-Path $prismRoot 'native-host.json'
$nativeJson=@{name='com.prism.widget';description='Prism quota-only browser sync';path=(Join-Path $prismRoot 'prism-native-host.cmd');type='stdio';allowed_origins=@("chrome-extension://$extensionId/")} | ConvertTo-Json
[IO.File]::WriteAllText($nativePath,$nativeJson,(New-Object Text.UTF8Encoding($false)))
foreach($browserKey in @('HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.prism.widget','HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.prism.widget')) {
    New-Item -Path $browserKey -Force | Out-Null
    Set-Item -LiteralPath $browserKey -Value $nativePath
}
Write-Host 'Prism browser connection registered for this Windows user.'
Write-Host '1. Open chrome://extensions or edge://extensions.'
Write-Host '2. Enable Developer mode, click Load unpacked, select the browser-extension folder shown below.'
Write-Host (Join-Path $prismRoot 'browser-extension')
Write-Host '3. Sign in at claude.ai in that browser. Open the Prism extension and click Refresh.'
Write-Host 'Quota then refreshes every 5 minutes without manual numbers.'
