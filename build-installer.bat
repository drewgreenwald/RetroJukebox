@echo off
setlocal
echo ============================================================
echo  RetroJukebox Installer Builder
echo ============================================================
echo.

:: Check for dotnet
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: dotnet CLI not found.
    echo        Install .NET 8 SDK from https://dotnet.microsoft.com
    pause ^& exit /b 1
)

:: Look for NSIS makensis.exe
set MAKENSIS=
if exist "%PROGRAMFILES(X86)%\NSIS\makensis.exe" set MAKENSIS="%PROGRAMFILES(X86)%\NSIS\makensis.exe"
if exist "%PROGRAMFILES%\NSIS\makensis.exe" set MAKENSIS="%PROGRAMFILES%\NSIS\makensis.exe"
where makensis >nul 2>&1
if %errorlevel% equ 0 set MAKENSIS=makensis

if "%MAKENSIS%"=="" (
    echo ERROR: NSIS not found.
    echo        Download from https://nsis.sourceforge.io/Download
    echo        Install it then run this script again.
    pause ^& exit /b 1
)
echo Found NSIS: %MAKENSIS%
echo.

:: Step 1 - Publish
echo [1/2] Publishing RetroJukebox...
cd /d "%~dp0RetroJukebox"
dotnet publish -c Release -r win-x64 --self-contained true -o "bin\Publish"
if %errorlevel% neq 0 (
    echo ERROR: Publish failed.
    pause ^& exit /b 1
)
echo    Publish complete.
echo.

:: Step 2 - Build installer
echo [2/2] Building installer...
cd /d "%~dp0Installer"
%MAKENSIS% RetroJukebox.nsi
if %errorlevel% neq 0 (
    echo ERROR: NSIS build failed.
    pause ^& exit /b 1
)
echo    Installer built.
echo.

echo ============================================================
echo  Done!
echo  Installer: %~dp0RetroJukebox-1.0.0-Setup.exe
echo ============================================================
echo.
pause
