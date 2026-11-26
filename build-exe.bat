@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo   .NET 8 SDK was not found.
    echo   Install it (free) from https://dotnet.microsoft.com/download/dotnet/8.0
    echo   then double-click this file again.
    echo.
    pause
    exit /b 1
)

echo.
echo   Drawing the app icon ...
if exist "%~dp0tools\make-icon.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-icon.ps1" -Out "%~dp0Assets\FastCraft.ico"
)

echo   Building FastCraft.exe ...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:ReadyToRun=true -o "%~dp0dist"
if errorlevel 1 (
    echo.
    echo   Build failed. The messages above say why.
    echo.
    pause
    exit /b 1
)

echo.
echo   Done: %~dp0dist\FastCraft.exe
echo   That file is self-contained: copy it anywhere and double-click it.
echo.
pause
