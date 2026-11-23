@echo off
setlocal

echo.
echo =====================================
echo  Extracting version number...
echo =====================================
echo.

set VERSION=0.0.0
for /f "delims=" %%i in ('powershell -NoProfile -Command "(Select-Xml -Path 'src\DotCraft.App\DotCraft.App.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i
if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" src\DotCraft.App\DotCraft.App.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)
for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building Satellite (tray client)...
echo =====================================
echo.

call dotnet tool restore
if %ERRORLEVEL% neq 0 (
    echo Restoring the Velopack CLI failed with exit code %ERRORLEVEL%.
    goto :failure
)

set SATELLITE_PUBLISH=src\DotCraft.Satellite\bin\publish\win-x64
set SATELLITE_RELEASE=src\DotCraft.Satellite\bin\velopack
if exist "%SATELLITE_PUBLISH%" rmdir /s /q "%SATELLITE_PUBLISH%"
if exist "%SATELLITE_RELEASE%" rmdir /s /q "%SATELLITE_RELEASE%"

call dotnet publish "src\DotCraft.Satellite\DotCraft.Satellite.csproj" -c Release -r win-x64 -o "%SATELLITE_PUBLISH%"
if %ERRORLEVEL% neq 0 (
    echo Satellite publish failed with exit code %ERRORLEVEL%.
    goto :failure
)

call dotnet vpk pack --packId DotCraft.Satellite --packVersion %VERSION% --packDir "%SATELLITE_PUBLISH%" --mainExe dotcraft-satellite.exe --packTitle "DotCraft Satellite" --packAuthors DotHarness --icon "src\DotCraft.Satellite\Assets\satellite.ico" --noPortable --delta none --outputDir "%SATELLITE_RELEASE%"
if %ERRORLEVEL% neq 0 (
    echo Satellite packaging failed with exit code %ERRORLEVEL%.
    goto :failure
)

if not exist "build\dotcraft" mkdir "build\dotcraft"
REM Hub serves this copy at /satellite/installer, so it must sit beside dotcraft.exe.
copy /Y "%SATELLITE_RELEASE%\DotCraft.Satellite-win-Setup.exe" "build\dotcraft\DotCraft-Satellite-Setup.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo Satellite installer was not produced.
    goto :failure
)

echo.
echo =====================================
echo  Satellite packaging completed!
echo =====================================
echo.
pause
exit /b 0

:failure
echo.
echo Satellite build failed. Please try again.
echo.
pause
exit /b 1
