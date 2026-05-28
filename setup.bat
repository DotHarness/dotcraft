@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_ARGS="
set "MODE=install"

:parse_args
if "%~1"=="" goto run_setup

if /I "%~1"=="/check" (
    set "PS_ARGS=%PS_ARGS% -Check"
    set "MODE=check"
    shift
    goto parse_args
)

if /I "%~1"=="--check" (
    set "PS_ARGS=%PS_ARGS% -Check"
    set "MODE=check"
    shift
    goto parse_args
)

if /I "%~1"=="/yes" (
    set "PS_ARGS=%PS_ARGS% -Yes"
    shift
    goto parse_args
)

if /I "%~1"=="--yes" (
    set "PS_ARGS=%PS_ARGS% -Yes"
    shift
    goto parse_args
)

if /I "%~1"=="/?" (
    set "PS_ARGS=%PS_ARGS% -Help"
    set "MODE=help"
    shift
    goto parse_args
)

if /I "%~1"=="--help" (
    set "PS_ARGS=%PS_ARGS% -Help"
    set "MODE=help"
    shift
    goto parse_args
)

echo Unknown argument: %~1
echo.
echo Usage:
echo   setup.bat          Check and prompt to install missing tools
echo   setup.bat /check   Check only
echo   setup.bat /yes     Install missing tools without confirmation
exit /b 2

:run_setup
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\setup-dev-env.ps1"%PS_ARGS%
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    if "%MODE%"=="check" (
        echo DotCraft developer environment check found missing tools.
    ) else (
        echo DotCraft developer environment setup failed.
    )
    exit /b %EXIT_CODE%
)

if "%MODE%"=="help" exit /b 0

echo.
if "%MODE%"=="check" (
    echo DotCraft developer environment check completed.
) else (
    echo DotCraft developer environment setup completed.
)
exit /b 0
