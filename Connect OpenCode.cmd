@echo off
title Prism - OpenCode sign-in
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\connect-opencode.ps1"
set "prismConnectExit=%errorlevel%"
if not "%prismConnectExit%"=="0" pause
exit /b %prismConnectExit%
