@echo off
pwsh -NoProfile -File "%~dp0build-compat.ps1" %*
exit /b %errorlevel%
