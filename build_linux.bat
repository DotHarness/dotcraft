@echo off

if exist "build\dotcraft" rmdir /s /q "build\dotcraft"
if exist "build\oratorio" rmdir /s /q "build\oratorio"
mkdir "build\dotcraft"
mkdir "build\oratorio"

cd src/DotCraft.App

echo.
echo =====================================
echo Extracting version number...
echo =====================================
echo.

set VERSION=0.0.0

for /f "delims=" %%i in ('powershell -Command "(Select-Xml -Path 'DotCraft.App.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i

if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" DotCraft.App.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)

for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building DotCraft (linux-x64)...
echo =====================================
echo.

call dotnet publish /p:PublishProfile=ReleaseProfile -r linux-x64 -o ..\..\build\dotcraft

if %ERRORLEVEL% neq 0 (
    echo Build failed with exit code %ERRORLEVEL%.
    goto :failure
)

goto :package

:failure
echo.
echo Build failed. Please try again.
echo.
pause
exit /b 1

:package
cd ../..

echo.
echo =====================================
echo  Building Oratorio Server (linux-x64)...
echo =====================================
echo.

call dotnet publish "src\Oratorio.Server\Oratorio.Server.csproj" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "build\oratorio"
if %ERRORLEVEL% neq 0 goto :failure

echo.
echo =====================================
echo  Staging Desktop bundled modules...
echo =====================================
echo.

if not exist "desktop\resources\modules\channel-feishu" mkdir "desktop\resources\modules\channel-feishu"
if not exist "desktop\resources\modules\channel-weixin" mkdir "desktop\resources\modules\channel-weixin"
if not exist "desktop\resources\modules\channel-telegram" mkdir "desktop\resources\modules\channel-telegram"
if not exist "desktop\resources\modules\channel-qq" mkdir "desktop\resources\modules\channel-qq"
if not exist "desktop\resources\modules\channel-wecom" mkdir "desktop\resources\modules\channel-wecom"

if exist "sdk\typescript\packages\channel-feishu\manifest.json" (
    copy /Y "sdk\typescript\packages\channel-feishu\manifest.json" "desktop\resources\modules\channel-feishu\manifest.json" >nul
    copy /Y "sdk\typescript\packages\channel-feishu\package.json" "desktop\resources\modules\channel-feishu\package.json" >nul
    xcopy /E /I /Y "sdk\typescript\packages\channel-feishu\dist" "desktop\resources\modules\channel-feishu\dist" >nul
) else (
    echo WARNING: channel-feishu manifest.json not found. Run sdk\typescript build first.
)

if exist "sdk\typescript\packages\channel-weixin\manifest.json" (
    copy /Y "sdk\typescript\packages\channel-weixin\manifest.json" "desktop\resources\modules\channel-weixin\manifest.json" >nul
    copy /Y "sdk\typescript\packages\channel-weixin\package.json" "desktop\resources\modules\channel-weixin\package.json" >nul
    xcopy /E /I /Y "sdk\typescript\packages\channel-weixin\dist" "desktop\resources\modules\channel-weixin\dist" >nul
) else (
    echo WARNING: channel-weixin manifest.json not found. Run sdk\typescript build first.
)

if exist "sdk\typescript\packages\channel-telegram\manifest.json" (
    copy /Y "sdk\typescript\packages\channel-telegram\manifest.json" "desktop\resources\modules\channel-telegram\manifest.json" >nul
    copy /Y "sdk\typescript\packages\channel-telegram\package.json" "desktop\resources\modules\channel-telegram\package.json" >nul
    xcopy /E /I /Y "sdk\typescript\packages\channel-telegram\dist" "desktop\resources\modules\channel-telegram\dist" >nul
) else (
    echo WARNING: channel-telegram manifest.json not found. Run sdk\typescript build first.
)

if exist "sdk\typescript\packages\channel-qq\manifest.json" (
    copy /Y "sdk\typescript\packages\channel-qq\manifest.json" "desktop\resources\modules\channel-qq\manifest.json" >nul
    copy /Y "sdk\typescript\packages\channel-qq\package.json" "desktop\resources\modules\channel-qq\package.json" >nul
    xcopy /E /I /Y "sdk\typescript\packages\channel-qq\dist" "desktop\resources\modules\channel-qq\dist" >nul
) else (
    echo WARNING: channel-qq manifest.json not found. Run sdk\typescript build first.
)

if exist "sdk\typescript\packages\channel-wecom\manifest.json" (
    copy /Y "sdk\typescript\packages\channel-wecom\manifest.json" "desktop\resources\modules\channel-wecom\manifest.json" >nul
    copy /Y "sdk\typescript\packages\channel-wecom\package.json" "desktop\resources\modules\channel-wecom\package.json" >nul
    xcopy /E /I /Y "sdk\typescript\packages\channel-wecom\dist" "desktop\resources\modules\channel-wecom\dist" >nul
) else (
    echo WARNING: channel-wecom manifest.json not found. Run sdk\typescript build first.
)

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

echo Creating dotcraft-linux-x64_v%VERSION%.tar.gz...
tar -czf "build\dotcraft\dotcraft-linux-x64_v%VERSION%.tar.gz" -C "build\dotcraft" dotcraft

if %ERRORLEVEL% neq 0 (
    echo Packaging failed with exit code %ERRORLEVEL%.
    goto :failure
)

echo Creating oratorio-linux-x64_v%VERSION%.tar.gz...
tar -czf "build\oratorio\oratorio-linux-x64_v%VERSION%.tar.gz" -C "build\oratorio" oratorio-server
if %ERRORLEVEL% neq 0 goto :failure

echo.
echo =====================================
echo  Build completed successfully!
echo =====================================
echo  - build\dotcraft
echo  - build\oratorio
echo =====================================
echo.
pause
