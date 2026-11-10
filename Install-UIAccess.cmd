@echo off
rem Double-click this. It runs Install-UIAccess.ps1 and asks for administrator rights once.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-UIAccess.ps1" %*
echo.
echo (The installer continues in the window that just opened. Its log is Install-UIAccess.log here.)
pause
