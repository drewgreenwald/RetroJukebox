@echo off
setlocal
echo ============================================================
echo  RetroJukebox Installer Builder
echo ============================================================
echo.

echo [1/2] Publishing RetroJukebox...
cd /d "%~dp0RetroJukebox"
dotnet publish -c Release -r win-x64 --self-contained true -o "bin\Publish"
if %errorlevel% neq 0 (
    echo ERROR: Publish failed.
    pause
    exit /b 1
)
echo    Publish complete.
echo.

echo [2/2] Building installer...
cd /d "%~dp0"
"C:\Program Files (x86)\NSIS\makensis.exe" Installer\RetroJukebox.nsi
if %errorlevel% neq 0 (
    echo ERROR: NSIS build failed.
    pause
    exit /b 1
)
echo    Installer built.
echo.

echo Done!
echo Installer: %~dp0RetroJukebox-1.0.0-Setup.exe
echo.
pause