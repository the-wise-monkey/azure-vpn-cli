@echo off
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0vpn.ps1" %*
exit /b %ERRORLEVEL%
