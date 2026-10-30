# Optional private OpenCode runtime. Does not change PATH or the user's CLI.
param([string]$ArchivePath)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop'){$env:PSModulePath=[IO.Path]::Combine($PSHOME,'Modules')}
$prismRoot=Split-Path $PSScriptRoot -Parent
$toolRoot=Join-Path $prismRoot 'data\tools\opencode-v2\2.2.2'
$archive=if($ArchivePath){[IO.Path]::GetFullPath($ArchivePath)}else{Join-Path $prismRoot 'build\opencode-2.2.2-windows-x64.zip'}
$checksum='e604e66cc13128fe36db7c68c7b0ed31c10b2ce759dd3d702eae080dc140bed5'
$binaryChecksum='e49134e14f29f0ab586035c3e6e486a7aa9c3e721d556661eff9f4eee0e1dae4'
$binary=Join-Path $toolRoot 'opencode.exe'
if(Test-Path -LiteralPath $binary){
    if((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $binaryChecksum){throw 'The existing private runtime differs from the pinned binary. It has been preserved; check it before reinstalling.'}
    Write-Host 'Verified private OpenCode v2.2.2 is already installed. Global CLI unchanged.'
    return
}
New-Item -ItemType Directory -Force -Path (Split-Path $archive -Parent) | Out-Null
if(-not (Test-Path -LiteralPath $archive)){
    if($ArchivePath){throw 'The supplied OpenCode archive does not exist.'}
    $download=$archive+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{
        Invoke-WebRequest 'https://github.com/anomalyco/opencode/releases/download/v2.2.2/opencode-windows-x64.zip' -OutFile $download -UseBasicParsing -TimeoutSec 300
        if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum){throw 'OpenCode download checksum mismatch. Installation stopped.'}
        [IO.File]::Move($download,$archive)
    }finally{if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}}
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum){throw 'OpenCode archive checksum mismatch. Installation stopped.'}
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $toolRoot
if((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $binaryChecksum){throw 'Extracted OpenCode binary checksum mismatch. Do not run it.'}
Write-Host 'Installed checksum-verified OpenCode v2.2.2 for Prism. Global CLI unchanged.'
