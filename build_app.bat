@echo off

if exist "build" (
    rmdir /s /q "build"
)
mkdir "build"

cd src/DotCraft.App

echo.
echo =====================================
echo Extracting version number...
echo =====================================
echo.

REM Set default version in case extraction fails
set VERSION=0.0.0

REM Extract version using PowerShell for better reliability
for /f "delims=" %%i in ('powershell -Command "(Select-Xml -Path 'DotCraft.App.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i

REM If PowerShell method failed, try manual parsing
if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" DotCraft.App.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)

REM Remove any whitespace
for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building DotCraft...
echo =====================================
echo.

call dotnet publish /p:PublishProfile=ReleaseProfile

if %ERRORLEVEL% neq 0 (
    echo Build DotCraft failed with exit code %ERRORLEVEL%.
    goto :failure
)

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
