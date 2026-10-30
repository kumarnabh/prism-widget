# Optional private OpenCode runtime. Does not change PATH or the user's CLI.
$ErrorActionPreference='Stop'
$prismRoot=Split-Path $PSScriptRoot -Parent
$toolRoot=Join-Path $prismRoot 'data\tools\opencode-v2\2.2.2'
$archive=Join-Path $prismRoot 'build\opencode-2.2.2-windows-x64.zip'
$checksum='e604e66cc13128fe36db7c68c7b0ed31c10b2ce759dd3d702eae080dc140bed5'
New-Item -ItemType Directory -Force -Path (Split-Path $archive -Parent) | Out-Null
if(-not (Test-Path -LiteralPath $archive)){
    Invoke-WebRequest 'https://github.com/anomalyco/opencode/releases/download/v2.2.2/opencode-windows-x64.zip' -OutFile $archive
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum){throw 'OpenCode archive checksum mismatch. Installation stopped.'}
if(Test-Path -LiteralPath (Join-Path $toolRoot 'opencode.exe')){throw 'Private runtime already exists; it has been preserved.'}
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $toolRoot
Write-Host 'Installed checksum-verified OpenCode v2.2.2 for Prism. Global CLI unchanged.'
