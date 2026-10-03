param([int]$TimeoutSeconds=60,[switch]$LiveAccounts)
$ErrorActionPreference='Stop'
$prismRoot=Split-Path $PSScriptRoot -Parent
$executable=Join-Path $prismRoot 'Prism.exe'
$resultPath=Join-Path $prismRoot 'selftest.json'
$started=[DateTime]::UtcNow
$mode=if($LiveAccounts){'--selftest'}else{'--offline-selftest'}
$process=Start-Process -FilePath $executable -ArgumentList $mode -WorkingDirectory $prismRoot -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit($TimeoutSeconds*1000)){
    $process.Kill()
    $fresh=@('selftest.json','preview-hd.png','preview-settings-fr.png','preview-capacity-fr.png','preview-history-fr.png','preview-tray-fr.png') | Where-Object {
        $file=Join-Path $prismRoot $_
        (Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -ge $started
    }
    throw "Native self-test timed out after $TimeoutSeconds seconds. Fresh fixed-name outputs: $($fresh -join ', ')."
}
if($process.ExitCode -ne 0){throw "Native self-test exited with code $($process.ExitCode)."}
if(-not (Test-Path -LiteralPath $resultPath)){throw 'Native self-test did not produce results.'}
if((Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $started){throw 'Native self-test results are stale.'}
$result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
foreach($check in @('pin','compact','sensors','responsive','providers','quotaValidation','presets','clocks','reminders','featureChecks','forecastChecks')){
    if($result.$check -ne $true){throw "Native check failed: $check"}
}
if($result.layouts.Count -ne 16){throw 'Expected eight layouts with clocks both on and off.'}
foreach($layout in $result.layouts){
    if($layout.fits -ne $true -or $layout.allReadingsVisible -ne $true -or $layout.scale -lt 0.999){throw "Layout failed: $($layout.width) x $($layout.height)"}
}
if($result.transitions.Count -ne 480){throw 'Expected 480 forward/reverse resize checks with clocks on and off.'}
foreach($layout in $result.transitions){
    if($layout.fits -ne $true -or $layout.controls -ne $true -or $layout.scale -lt 0.999){throw "Resize transition failed: $($layout.width) x $($layout.height), clocks=$($layout.clocksEnabled), controls=$($layout.controls), scale=$($layout.scale)"}
}
if($result.customized.Count -ne 160){throw 'Expected 160 language, clock format and metric selection checks.'}
foreach($layout in $result.customized){
    if($layout.fits -ne $true -or $layout.controls -ne $true -or $layout.selection -ne $true -or $layout.scale -lt 0.999){throw "Customized layout failed: $($layout.language), 24h=$($layout.use24), metrics=$($layout.metrics), $($layout.width) x $($layout.height), controls=$($layout.controls), scale=$($layout.scale)"}
}
$hdPath=Join-Path $prismRoot 'preview-hd.png'
if(-not (Test-Path -LiteralPath $hdPath) -or (Get-Item -LiteralPath $hdPath).LastWriteTimeUtc -lt $started){throw 'HD preview missing or stale.'}
$png=[IO.File]::ReadAllBytes($hdPath)
if($png.Length -lt 24 -or [BitConverter]::ToString($png,0,8) -ne '89-50-4E-47-0D-0A-1A-0A'){throw 'Invalid HD PNG.'}
function Read-PngDimension([int]$offset){return [long]$png[$offset]*16777216+[long]$png[$offset+1]*65536+[long]$png[$offset+2]*256+$png[$offset+3]}
$hdWidth=Read-PngDimension 16
$hdHeight=Read-PngDimension 20
if($result.hdLogicalWidth -le 0 -or $result.hdLogicalHeight -le 0 -or $hdWidth -ne [Math]::Ceiling($result.hdLogicalWidth*4) -or $hdHeight -ne [Math]::Ceiling($result.hdLogicalHeight*4)){throw 'HD preview does not match the 4x logical layout resolution.'}
Write-Host "Native checks passed in $([Math]::Round(([DateTime]::UtcNow-$started).TotalSeconds,1))s: sensors, controls, 16 layouts, 480 resize transitions, 160 customized layouts, isolated feature/forecast checks, and $hdWidth x $hdHeight HD export."
