@echo off
rem Builds Screenshot.exe with the C# compiler that is already part of Windows.
rem Nothing to install, no third-party code.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x
  pause
  exit /b 1
)

rem Stop a running copy so the exe can be overwritten.
taskkill /f /im Screenshot.exe >nul 2>&1

"%CSC%" /nologo /optimize+ /target:winexe /out:Screenshot.exe /win32manifest:app.manifest ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  Screenshot.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Built Screenshot.exe - starting it now.
start "" "%~dp0Screenshot.exe"
