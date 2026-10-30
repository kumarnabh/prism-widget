@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
set "prismSetupExit=%errorlevel%"
if not "%prismSetupExit%"=="0" pause
exit /b %prismSetupExit%
