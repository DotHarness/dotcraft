@echo off
echo.
echo =====================================
echo  Building TUI (dotcraft-tui)...
echo =====================================
echo.

cd tui
call cargo build --release
if %ERRORLEVEL% neq 0 (
    echo TUI build failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
copy /Y "target\release\dotcraft-tui.exe" "..\build\release\dotcraft-tui.exe"

goto :success

:failure
echo.
echo Installation failed. Please try again.
echo.
pause
exit /b 1

:success
echo.
echo =====================================
echo  Build completed successfully!
echo =====================================
echo.

pause 