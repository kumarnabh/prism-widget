@echo off
if exist "%~dp0.venv\Scripts\python.exe" (
  "%~dp0.venv\Scripts\python.exe" "%~dp0native_host.py" %*
) else (
  python "%~dp0native_host.py" %*
)
