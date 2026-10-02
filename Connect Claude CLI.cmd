@echo off
title Prism - Claude CLI sign-in
cd /d "%~dp0"
if exist "%~dp0.venv\Scripts\python.exe" (
  "%~dp0.venv\Scripts\python.exe" "%~dp0claude_cli.py" --login
) else (
  python "%~dp0claude_cli.py" --login
)
echo.
pause
