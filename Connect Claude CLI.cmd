@echo off
title Prism - Claude CLI sign-in
cd /d "%~dp0"
python "%~dp0claude_cli.py" --login
echo.
pause
