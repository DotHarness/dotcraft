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

cd ../..

echo.
echo =====================================
echo  Building TypeScript SDK...
echo =====================================
echo.

cd sdk\typescript
call npm ci --prefer-offline
if %ERRORLEVEL% neq 0 (
    echo TypeScript SDK npm ci failed with exit code %ERRORLEVEL%.
    cd ..\..
    goto :failure
)
call npm run build:all
if %ERRORLEVEL% neq 0 (
    echo TypeScript SDK build failed with exit code %ERRORLEVEL%.
    cd ..\..
    goto :failure
)
cd ..\..

echo.
echo =====================================
echo  Building Desktop (dotcraft-desktop)...
echo =====================================
echo.

cd desktop
if exist resources\bin (
    rmdir /s /q resources\bin
)
mkdir resources\bin
copy /Y "..\build\dotcraft\dotcraft.exe" "resources\bin\dotcraft.exe"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage embedded dotcraft.exe for Desktop build.
    cd ..
    goto :failure
)
echo Staging computer use driver...
node scripts\stage-cua-driver.mjs --platform win32 --arch x64
if %ERRORLEVEL% neq 0 (
    echo Failed to stage the computer use driver for Desktop build.
    cd ..
    goto :failure
)
if exist dist (
    rmdir /s /q dist
)
call npm ci --prefer-offline --omit=peer
if %ERRORLEVEL% neq 0 (
    echo Desktop npm ci failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
call powershell -NoProfile -ExecutionPolicy Bypass -File "..\scripts\repair-electron-builder-cache.ps1"
if %ERRORLEVEL% neq 0 (
    echo Desktop electron-builder cache repair failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
call npm run dist:package -- --win --x64
if %ERRORLEVEL% neq 0 (
    echo Desktop build failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
cd ..

REM Copy desktop NSIS installer output to build/dotcraft/
echo Copying desktop artifacts to build\dotcraft\...
for %%f in (desktop\dist\*Setup*.exe) do (
    copy /Y "%%f" "build\dotcraft\" >nul 2>&1
)

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
REM Hub serves this copy at /satellite/installer, so it must sit beside dotcraft.exe.
copy /Y "%SATELLITE_RELEASE%\DotCraft.Satellite-win-Setup.exe" "build\dotcraft\DotCraft-Satellite-Setup.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo Satellite installer was not produced.
    goto :failure
)

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

REM Copy helper scripts to CLI output directory
copy /Y "Scripts\install_to_path.ps1" "build\dotcraft\install_to_path.ps1"

echo.
echo =====================================
echo  Packaging completed!
echo =====================================
echo.
pause
