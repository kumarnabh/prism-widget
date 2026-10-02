param([int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
$prismRoot=Split-Path $PSScriptRoot -Parent
$executable=Join-Path $prismRoot 'Prism.exe'
$resultPath=Join-Path $prismRoot 'selftest.json'
$started=[DateTime]::UtcNow
$process=Start-Process -FilePath $executable -ArgumentList '--selftest' -WorkingDirectory $prismRoot -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit($TimeoutSeconds*1000)){
    $process.Kill()
    throw 'Native self-test timed out.'
}
if($process.ExitCode -ne 0){throw "Native self-test exited with code $($process.ExitCode)."}
if(-not (Test-Path -LiteralPath $resultPath)){throw 'Native self-test did not produce results.'}
if((Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $started){throw 'Native self-test results are stale.'}
$result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
foreach($check in @('pin','compact','sensors','responsive','providers','quotaValidation','presets')){
    if($result.$check -ne $true){throw "Native check failed: $check"}
}
if($result.layouts.Count -ne 8){throw 'Expected eight layout checks.'}
foreach($layout in $result.layouts){
    if($layout.fits -ne $true -or $layout.allReadingsVisible -ne $true -or $layout.scale -lt 0.999){throw "Layout failed: $($layout.width) x $($layout.height)"}
}
$hdPath=Join-Path $prismRoot 'preview-hd.png'
if(-not (Test-Path -LiteralPath $hdPath) -or (Get-Item -LiteralPath $hdPath).LastWriteTimeUtc -lt $started){throw 'HD preview missing or stale.'}
$png=[IO.File]::ReadAllBytes($hdPath)
if($png.Length -lt 24 -or [BitConverter]::ToString($png,0,8) -ne '89-50-4E-47-0D-0A-1A-0A'){throw 'Invalid HD PNG.'}
function Read-PngDimension([int]$offset){return [long]$png[$offset]*16777216+[long]$png[$offset+1]*65536+[long]$png[$offset+2]*256+$png[$offset+3]}
$hdWidth=Read-PngDimension 16
$hdHeight=Read-PngDimension 20
if($hdWidth -lt 1700 -or $hdHeight -lt 3400){throw 'HD preview is below the required export resolution.'}
Write-Host "Native checks passed: fresh results, sensors, controls, eight layouts, and $hdWidth x $hdHeight HD export."
