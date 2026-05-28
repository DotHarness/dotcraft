@echo off
setlocal

set "VERSION=%~1"
if "%VERSION%"=="" (
    set /p VERSION=Enter new version ^(X.Y.Z^): 
)

if "%VERSION%"=="" (
    echo Error: version is required.
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1" "%VERSION%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Version bump failed.
    exit /b %EXIT_CODE%
)

echo.
echo Version bump succeeded: %VERSION%
echo Next check: git diff
exit /b 0
