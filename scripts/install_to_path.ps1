# DotCraft - Install to System PATH

$ErrorActionPreference = "Stop"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "DotCraft Installer" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Get the directory where this script is located
$installDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($installDir)) {
    $installDir = Get-Location
}

Write-Host "Installation directory: $installDir" -ForegroundColor Cyan
Write-Host ""

# Check if required files exist
$exePath = Join-Path $installDir "dotcraft.exe"
$tuiPath = Join-Path $installDir "dotcraft-tui.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Error: Missing required file: dotcraft.exe" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please ensure dotcraft.exe is in the same folder" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

Write-Host "Found: dotcraft.exe" -ForegroundColor Green
if (Test-Path $tuiPath) {
    Write-Host "Found: dotcraft-tui.exe (TUI client)" -ForegroundColor Green
} else {
    Write-Host "Note: dotcraft-tui.exe not found (optional TUI client)" -ForegroundColor Gray
}
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "Adding to system PATH..." -ForegroundColor Green
$pathScope = if ($isAdmin) { "Machine" } else { "User" }
Write-Host "Scope: $pathScope PATH" -ForegroundColor Cyan

if (-not $isAdmin) {
    Write-Host "Note: Running as user (not administrator)" -ForegroundColor Yellow
    Write-Host "      Will modify user PATH only" -ForegroundColor Yellow
}

Write-Host ""

# Get current PATH
$currentPath = [Environment]::GetEnvironmentVariable("Path", $pathScope)

# Check if already in PATH
if ($currentPath -like "*$installDir*") {
    Write-Host "This directory is already in $pathScope PATH" -ForegroundColor Yellow
} else {
    # Add to PATH
    $newPath = "$currentPath;$installDir"
    [Environment]::SetEnvironmentVariable("Path", $newPath, $pathScope)
    Write-Host "Successfully added to $pathScope PATH!" -ForegroundColor Green
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Installation Complete!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Commands available from PATH:" -ForegroundColor Cyan
Write-Host "  - dotcraft       (CLI / AppServer launcher)" -ForegroundColor White
if (Test-Path $tuiPath) {
    Write-Host "  - dotcraft-tui   (TUI client, same directory)" -ForegroundColor White
}
Write-Host ""
Write-Host "IMPORTANT: " -ForegroundColor Yellow -NoNewline
Write-Host "Please restart your terminal for PATH changes to take effect" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
