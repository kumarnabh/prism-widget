param([switch]$SkipDependencies)
$ErrorActionPreference='Stop'
$prismRoot=$PSScriptRoot
$buildRoot=Join-Path $prismRoot 'build'
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
if(-not $SkipDependencies){
    python -m pip install --disable-pip-version-check --target (Join-Path $prismRoot 'vendor') -r (Join-Path $prismRoot 'requirements.txt')
    if($LASTEXITCODE -ne 0){throw 'Dependency installation failed'}
}
dotnet build (Join-Path $prismRoot 'source/Prism.csproj') -c Release -o (Join-Path $buildRoot 'release') --nologo
if($LASTEXITCODE -ne 0){throw 'Build failed'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $buildRoot 'release') -Filter 'Prism*' -File){Copy-Item -LiteralPath $file.FullName -Destination $prismRoot -Force}
Write-Host 'Built Prism.exe. Run it from this folder.'
