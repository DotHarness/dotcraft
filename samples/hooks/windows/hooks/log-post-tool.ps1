$inputData = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($inputData)) {
    exit 0
}

$payload = $inputData | ConvertFrom-Json
$toolName = [string]$payload.toolName
$filePath = ""

if ($null -ne $payload.toolArgs) {
    if ($null -ne $payload.toolArgs.path) {
        $filePath = [string]$payload.toolArgs.path
    }
    elseif ($null -ne $payload.toolArgs.filePath) {
        $filePath = [string]$payload.toolArgs.filePath
    }
}

if ([string]::IsNullOrWhiteSpace($filePath)) {
    $filePath = "(unknown path)"
}

$logDir = Join-Path (Get-Location) ".craft/hooks"
$logPath = Join-Path $logDir "hooks.log"

if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$line = "$timestamp`t$toolName`t$filePath"
[System.IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [System.Text.Encoding]::UTF8)

exit 0
