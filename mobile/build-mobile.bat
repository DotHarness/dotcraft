@echo off
setlocal

for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "OUTPUT=%ROOT%\build\mobile"

where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is required. Install Node.js 22 and try again.
    exit /b 1
)
where npm >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm is required.
    exit /b 1
)
if defined JAVA_HOME (
    if not exist "%JAVA_HOME%\bin\java.exe" (
        echo ERROR: JAVA_HOME does not point to a JDK. JDK 17 is required.
        exit /b 1
    )
) else (
    where java >nul 2>&1
    if errorlevel 1 (
        echo ERROR: JDK 17 is required. Set JAVA_HOME and try again.
        exit /b 1
    )
)

if not defined ANDROID_HOME if defined ANDROID_SDK_ROOT set "ANDROID_HOME=%ANDROID_SDK_ROOT%"
if defined ANDROID_HOME (
    if not exist "%ANDROID_HOME%\build-tools" (
        echo ERROR: ANDROID_HOME must point to an Android SDK with build-tools installed.
        exit /b 1
    )
    if not exist "%ANDROID_HOME%\platforms" (
        echo ERROR: Install Android SDK platforms before building.
        exit /b 1
    )
) else if not exist "%~dp0android\local.properties" (
    echo ERROR: Set ANDROID_HOME to your Android SDK, or configure android\local.properties.
    exit /b 1
)

pushd "%ROOT%\sdk\typescript"
if errorlevel 1 exit /b 1

echo Building TypeScript SDK and avatar...
call npm ci --prefer-offline
if errorlevel 1 goto :failure
call npm run build
if errorlevel 1 goto :failure
call npm run build --workspace @dotcraft/avatar
if errorlevel 1 goto :failure

cd /d "%~dp0"
if errorlevel 1 goto :failure

echo Installing mobile dependencies...
call npm ci --prefer-offline
if errorlevel 1 goto :failure

set "VERSION="
for /f "delims=" %%V in ('node -p "require('./app.json').expo.version"') do set "VERSION=%%V"
if not defined VERSION (
    echo ERROR: Could not read the mobile app version from app.json.
    goto :failure
)

echo Generating Android project...
call npm exec -- expo prebuild --platform android --no-install
if errorlevel 1 goto :failure

cd /d "%~dp0android"
if errorlevel 1 goto :failure

echo Building Android Release APK...
call .\gradlew.bat assembleRelease -PreactNativeArchitectures=arm64-v8a,armeabi-v7a
if errorlevel 1 goto :failure

if not exist "app\build\outputs\apk\release\app-release.apk" (
    echo ERROR: Gradle did not produce app-release.apk.
    goto :failure
)
if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if not exist "%OUTPUT%" goto :failure
copy /y "app\build\outputs\apk\release\app-release.apk" "%OUTPUT%\DotCraft-v%VERSION%-android.apk" >nul
if errorlevel 1 goto :failure

echo.
echo Build completed: %OUTPUT%\DotCraft-v%VERSION%-android.apk
echo WARNING: This local APK uses the generated project's signing configuration.
echo The default Expo configuration uses a debug key; use a release key for distribution.
popd
exit /b 0

:failure
echo.
echo ERROR: Mobile build failed. No new APK was archived.
popd
exit /b 1
