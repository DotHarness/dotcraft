$inputData = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($inputData)) {
    exit 0
}

$payload = $inputData | ConvertFrom-Json
$commandText = ""

if ($null -ne $payload.toolArgs -and $null -ne $payload.toolArgs.command) {
    $commandText = [string]$payload.toolArgs.command
}

if ([string]::IsNullOrWhiteSpace($commandText)) {
    exit 0
}

$dangerousPatterns = @(
    'rm\s+-[rf]{1,2}\b',
    'Remove-Item\b.*-Recurse\b',
    'del\s+/[fqs]\b',
    'rmdir\s+/s\b',
    '\b(format|mkfs|diskpart)\b',
    '\b(Stop-Computer|Restart-Computer|shutdown|reboot|poweroff)\b'
)

foreach ($pattern in $dangerousPatterns) {
    if ($commandText -match $pattern) {
        [Console]::Error.WriteLine("Blocked dangerous Exec command: $commandText")
        exit 2
    }
}

exit 0
