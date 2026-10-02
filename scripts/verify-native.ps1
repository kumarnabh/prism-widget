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
foreach($check in @('pin','compact','sensors','responsive','providers')){
    if($result.$check -ne $true){throw "Native check failed: $check"}
}
if($result.layouts.Count -ne 8){throw 'Expected eight layout checks.'}
foreach($layout in $result.layouts){
    if($layout.fits -ne $true -or $layout.allReadingsVisible -ne $true){throw "Layout failed: $($layout.width) x $($layout.height)"}
}
Write-Host 'Native checks passed: fresh results, sensors, controls, and eight layouts.'
