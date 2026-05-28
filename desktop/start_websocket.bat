@echo off

setlocal
pushd "%~dp0" || exit /b 1
call npm run dev:websocket
set "exitCode=%ERRORLEVEL%"
popd
exit /b %exitCode%
