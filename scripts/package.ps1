param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$prismRoot=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $prismRoot 'build\packages'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
[xml]$project=Get-Content -LiteralPath (Join-Path $prismRoot 'source\Prism.csproj')
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
$stage=Join-Path $prismRoot ('build\package-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet publish (Join-Path $prismRoot 'source\Prism.csproj') -c Release -r win-x64 --self-contained true -o $stage --nologo -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
# Explicit source allowlist: never copy the installation folder wholesale.
$files=@('Setup.ps1','Setup.cmd','Launch Prism.cmd','Connect Claude CLI.cmd','Install Claude Browser Sync.cmd',
    'prism-native-host.cmd','install-browser-sync.ps1','providers.py','auto_sources.py','claude_cli.py',
    'claude_feed.py','native_host.py','quota_cache.py','history_scope.py','doctor.py','requirements.txt',
    'README.md','README.html','LICENSE','PRIVACY.md','SECURITY.md','CONTRIBUTING.md','AUDIT.md','TROUBLESHOOTING.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md')
foreach($name in $files){Copy-Item -LiteralPath (Join-Path $prismRoot $name) -Destination $stage}
foreach($folder in @('browser-extension','assets','docs\legal')){
    $destination=Join-Path $stage $folder
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    # Only public assets, extension source, and license texts belong here.
    $allowed=if($folder -eq 'assets'){@('prism.ico','prism-icon.png','icon-prompt.txt')}elseif($folder -eq 'browser-extension'){@('manifest.json','background.js','popup.html','popup.js')}else{@('dotnet-LICENSE.txt','dotnet-THIRD-PARTY-NOTICES.txt','wpf-LICENSE.txt','wpf-THIRD-PARTY-NOTICES.txt')}
    foreach($name in $allowed){Copy-Item -LiteralPath (Join-Path (Join-Path $prismRoot $folder) $name) -Destination $destination}
}
foreach($name in @('ARCHITECTURE.md','RELEASING.md','FORECASTING.md')){Copy-Item -LiteralPath (Join-Path $prismRoot "docs\$name") -Destination (Join-Path $stage 'docs')}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive=Join-Path $OutputDirectory "Prism-$version-win-x64.zip"
if(Test-Path -LiteralPath $archive){throw 'Release archive already exists; choose a new output directory to preserve it.'}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),"$hash  $([IO.Path]::GetFileName($archive))`n",[Text.UTF8Encoding]::new($false))
Write-Host "Package: $archive"
Write-Host "Scan directory: $stage"
