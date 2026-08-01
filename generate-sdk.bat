@echo off
setlocal

pushd "%~dp0"
dotnet run --project tools\DotCraft.ProtocolGen\DotCraft.ProtocolGen.csproj -- generate %*
set "EXIT_CODE=%ERRORLEVEL%"
popd

exit /b %EXIT_CODE%
