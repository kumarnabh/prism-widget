$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop'){$env:PSModulePath=[IO.Path]::Combine($PSHOME,'Modules')}
$prismRoot=Split-Path $PSScriptRoot -Parent
# Verify the pinned runtime, or install it once. No Python setup or global CLI changes.
& (Join-Path $PSScriptRoot 'install-opencode.ps1')
$binary=Join-Path $prismRoot 'data\tools\opencode-v2\2.2.2\opencode.exe'
# Ignore inherited server/config overrides while retaining the normal account data location.
Get-ChildItem Env: | Where-Object Name -Like 'OPENCODE_*' | ForEach-Object {Remove-Item -LiteralPath "Env:$($_.Name)"}
$env:OPENCODE_DISABLE_PROJECT_CONFIG='true'
$env:OPENCODE_DISABLE_DEFAULT_PLUGINS='true'
$env:OPENCODE_DISABLE_EXTERNAL_SKILLS='true'
$env:OPENCODE_DISABLE_AUTOUPDATE='true'
Set-Location -LiteralPath $prismRoot
Write-Host 'Complete OpenCode sign-in below. This window closes when sign-in finishes.'
Write-Host 'Then enable existing-sign-in reuse in Prism Connections and save. No terminal needs to stay open.'
& $binary auth login opencode --standalone --log-level none
if($LASTEXITCODE -ne 0){throw 'OpenCode sign-in did not complete. Run Connect OpenCode again to retry.'}
