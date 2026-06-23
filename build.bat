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
cd ..

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
copy /Y "..\build\release\dotcraft.exe" "resources\bin\dotcraft.exe"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage embedded dotcraft.exe for Desktop build.
    cd ..
    goto :failure
)
if exist resources\modules (
    rmdir /s /q resources\modules
)
mkdir resources\modules\channel-feishu
mkdir resources\modules\channel-weixin
mkdir resources\modules\channel-telegram
mkdir resources\modules\channel-qq
mkdir resources\modules\channel-wecom
copy /Y "..\sdk\typescript\packages\channel-feishu\manifest.json" "resources\modules\channel-feishu\manifest.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-feishu manifest.json for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-feishu\package.json" "resources\modules\channel-feishu\package.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-feishu package.json for Desktop build.
    cd ..
    goto :failure
)
xcopy /E /I /Y "..\sdk\typescript\packages\channel-feishu\dist" "resources\modules\channel-feishu\dist" >nul
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-feishu dist artifacts for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-weixin\manifest.json" "resources\modules\channel-weixin\manifest.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-weixin manifest.json for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-weixin\package.json" "resources\modules\channel-weixin\package.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-weixin package.json for Desktop build.
    cd ..
    goto :failure
)
xcopy /E /I /Y "..\sdk\typescript\packages\channel-weixin\dist" "resources\modules\channel-weixin\dist" >nul
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-weixin dist artifacts for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-telegram\manifest.json" "resources\modules\channel-telegram\manifest.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-telegram manifest.json for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-telegram\package.json" "resources\modules\channel-telegram\package.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-telegram package.json for Desktop build.
    cd ..
    goto :failure
)
xcopy /E /I /Y "..\sdk\typescript\packages\channel-telegram\dist" "resources\modules\channel-telegram\dist" >nul
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-telegram dist artifacts for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-qq\manifest.json" "resources\modules\channel-qq\manifest.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-qq manifest.json for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-qq\package.json" "resources\modules\channel-qq\package.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-qq package.json for Desktop build.
    cd ..
    goto :failure
)
xcopy /E /I /Y "..\sdk\typescript\packages\channel-qq\dist" "resources\modules\channel-qq\dist" >nul
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-qq dist artifacts for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-wecom\manifest.json" "resources\modules\channel-wecom\manifest.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-wecom manifest.json for Desktop build.
    cd ..
    goto :failure
)
copy /Y "..\sdk\typescript\packages\channel-wecom\package.json" "resources\modules\channel-wecom\package.json"
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-wecom package.json for Desktop build.
    cd ..
    goto :failure
)
xcopy /E /I /Y "..\sdk\typescript\packages\channel-wecom\dist" "resources\modules\channel-wecom\dist" >nul
if %ERRORLEVEL% neq 0 (
    echo Failed to stage channel-wecom dist artifacts for Desktop build.
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
call npm run dist
if %ERRORLEVEL% neq 0 (
    echo Desktop build failed with exit code %ERRORLEVEL%.
    cd ..
    goto :failure
)
cd ..

REM Copy desktop NSIS installer output to build/release/
echo Copying desktop artifacts to build\release\...
for %%f in (desktop\dist\*Setup*.exe) do (
    copy /Y "%%f" "build\release\" >nul 2>&1
)

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

REM Copy helper scripts to CLI output directory
copy /Y "Scripts\install_to_path.ps1" "build\release\install_to_path.ps1"

echo.
echo =====================================
echo  Packaging completed!
echo =====================================
echo.
pause 
