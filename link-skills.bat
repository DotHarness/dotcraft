@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
echo Select where to link DotCraft bundled skills and registry workflow skills.
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\link-skills.ps1" %*

if errorlevel 1 (
    echo.
    echo Linking failed.
    pause
    exit /b 1
)

echo.
echo DotCraft skill linking complete.
pause
