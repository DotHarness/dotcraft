param(
    [Parameter(Mandatory)][string]$BaseUrl,
    [Parameter(Mandatory)][string]$Token
)

$ErrorActionPreference = 'Stop'
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(2)
$timeout = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(30))
$sockets = [System.Collections.Generic.List[System.Net.WebSockets.ClientWebSocket]]::new()

function Get-Status([string]$Path, [string]$Bearer = '') {
    $request = [System.Net.Http.HttpRequestMessage]::new('GET', "$BaseUrl$Path")
    if ($Bearer) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Bearer)
    }
    try {
        $response = $http.SendAsync($request, $timeout.Token).GetAwaiter().GetResult()
        try { return [int]$response.StatusCode }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Connect-Socket([string]$Path, [string]$Bearer = '') {
    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    $sockets.Add($socket)
    if ($Bearer) { $socket.Options.SetRequestHeader('Authorization', "Bearer $Bearer") }
    $uri = [UriBuilder]::new("$BaseUrl$Path")
    $uri.Scheme = if ($uri.Scheme -eq 'https') { 'wss' } else { 'ws' }
    $null = $socket.ConnectAsync($uri.Uri, $timeout.Token).GetAwaiter().GetResult()
    return $socket
}

function Receive-Message($Socket) {
    $buffer = [byte[]]::new(16384)
    $stream = [System.IO.MemoryStream]::new()
    try {
        do {
            $result = $Socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                throw 'Relay closed the tunnel unexpectedly.'
            }
            $stream.Write($buffer, 0, $result.Count)
        } until ($result.EndOfMessage)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose() }
}

function Assert-Transfer($Sender, $Receiver, [int]$Size) {
    $payload = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($Size)
    $null = $Sender.SendAsync(
        [ArraySegment[byte]]::new($payload),
        [System.Net.WebSockets.WebSocketMessageType]::Binary,
        $true,
        $timeout.Token
    ).GetAwaiter().GetResult()
    $received = Receive-Message $Receiver
    if ([Convert]::ToBase64String($payload) -cne [Convert]::ToBase64String($received)) {
        throw 'Relay changed the tunnel payload.'
    }
}

try {
    do {
        try {
            $ready = (Get-Status '/r/host?host=smoke') -eq 401
        }
        catch {
            $ready = $false
        }
        if (!$ready) {
            $timeout.Token.ThrowIfCancellationRequested()
            Start-Sleep -Milliseconds 100
        }
    } until ($ready)

    foreach ($path in @('/r/host?host=smoke', '/r/accept?tunnel=unknown')) {
        if ((Get-Status $path) -ne 401 -or (Get-Status $path 'wrong-token') -ne 401) {
            throw "Relay accepted an unauthorized request: $path"
        }
    }

    $hostSocket = Connect-Socket '/r/host?host=smoke' $Token
    # The handshake can finish before the registration reaches the host table.
    do {
        $registered = (Get-Status '/r/connect?host=smoke') -eq 400
        if (!$registered) {
            $timeout.Token.ThrowIfCancellationRequested()
            Start-Sleep -Milliseconds 100
        }
    } until ($registered)

    $phone = Connect-Socket '/r/connect?host=smoke'
    $message = [System.Text.Encoding]::UTF8.GetString((Receive-Message $hostSocket)) | ConvertFrom-Json
    if ($message.type -ne 'open' -or !$message.tunnel) { throw 'Relay did not request a tunnel.' }
    $accepted = Connect-Socket "/r/accept?tunnel=$($message.tunnel)" $Token
    Assert-Transfer $phone $accepted 100000
    Assert-Transfer $accepted $phone 50000
    Write-Host 'Relay smoke test passed: authentication and bidirectional tunnel forwarding.'
}
finally {
    foreach ($socket in $sockets) { $socket.Dispose() }
    $timeout.Dispose()
    $http.Dispose()
}
