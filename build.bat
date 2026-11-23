@echo off
setlocal

set FORCE_CLEAN=0
if /i "%~1"=="--clean" set FORCE_CLEAN=1

if exist "build" (
    rmdir /s /q "build"
)
mkdir "build"

echo.
echo =====================================
echo  Building DotCraft...
echo =====================================
echo.

cd src/DotCraft.App
call dotnet publish /p:PublishProfile=ReleaseProfile
if %ERRORLEVEL% neq 0 (
    echo Build DotCraft failed with exit code %ERRORLEVEL%.
    cd ../..
    goto :failure
)
cd ../..

echo.
echo =====================================
echo  Building TypeScript SDK...
echo =====================================
echo.

cd sdk\typescript
if "%FORCE_CLEAN%"=="1" (
    call npm ci --prefer-offline
) else if not exist "node_modules" (
    call npm ci --prefer-offline
) else (
    echo Skipping npm ci in sdk\typescript ^(node_modules exists; pass --clean to force^).
)
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
if "%FORCE_CLEAN%"=="1" (
    call npm ci --prefer-offline --omit=peer
) else if not exist "node_modules" (
    call npm ci --prefer-offline --omit=peer
) else (
    echo Skipping npm ci in desktop ^(node_modules exists; pass --clean to force^).
)
if %ERRORLEVEL% neq 0 (
    echo Desktop npm ci failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
if "%FORCE_CLEAN%"=="1" (
    call powershell -NoProfile -ExecutionPolicy Bypass -File "..\scripts\repair-electron-builder-cache.ps1"
    if %ERRORLEVEL% neq 0 (
        echo Desktop electron-builder cache repair failed with exit code %ERRORLEVEL%.
        cd ..
        goto :failure
    )
)
call npm run dist:package -- --win --x64
if %ERRORLEVEL% neq 0 (
    echo Desktop build failed with exit code %ERRORLEVEL%.
    echo Tip: if this was caused by stale dependencies or a broken electron-builder cache, retry with "build.bat --clean".
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
exit /b 0

:failure
echo.
echo Build failed. Please try again.
echo.
pause
exit /b 1
