param([switch]$SkipLaunch)
$ErrorActionPreference='Stop'
$prismRoot=$PSScriptRoot
$python=Join-Path $prismRoot '.venv\Scripts\python.exe'
if(-not (Test-Path -LiteralPath $python)){
    $candidate=$null
    foreach($command in @('py','python')){
        $available=Get-Command $command -ErrorAction SilentlyContinue
        if(-not $available -or $available.Source -like '*\WindowsApps\*'){continue}
        $probeCode="import sys,struct,json; print(json.dumps([sys.executable,sys.version_info[:2],struct.calcsize('P')*8]))"
        $probe=if($command -eq 'py'){& $available.Source -3 -c $probeCode 2>$null}else{& $available.Source -c $probeCode 2>$null}
        if($LASTEXITCODE -ne 0){continue}
        try{$info=$probe | ConvertFrom-Json}catch{continue}
        if($info[1][0] -eq 3 -and $info[1][1] -ge 11 -and $info[1][1] -le 14 -and $info[2] -eq 64){$candidate=$info[0];break}
    }
    if(-not $candidate){throw 'Install 64-bit Python 3.11–3.14 from python.org (include the Python launcher), then run Setup again.'}
    & $candidate -m venv (Join-Path $prismRoot '.venv')
    if($LASTEXITCODE -ne 0){throw 'Could not create the local Python environment. Extract Prism to a writable folder.'}
}
& $python -m pip install --disable-pip-version-check --require-virtualenv -r (Join-Path $prismRoot 'requirements.txt')
if($LASTEXITCODE -ne 0){throw 'Dependency installation failed. Check your network and retry Setup.'}
& $python (Join-Path $prismRoot 'doctor.py')
if(-not (Test-Path -LiteralPath (Join-Path $prismRoot 'Prism.exe'))){
    Write-Host 'Python is ready. For a source checkout, run Build.ps1 -SkipDependencies with the .NET 8 SDK installed.'
    return
}
Write-Host 'Prism is ready. No startup task, administrator access or provider credential is required.'
if(-not $SkipLaunch){Start-Process -FilePath (Join-Path $prismRoot 'Prism.exe') -WorkingDirectory $prismRoot -WindowStyle Hidden}
