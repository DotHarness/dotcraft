using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpStreamableHttpSessionRecoveryIntegrationTests
{
    [Fact]
    public async Task StableBaseline_StartsWithInitializeAndNeverProbesDiscovery()
    {
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options
        {
            SupportsModernDiscovery = true
        });
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([CreateServerConfig(server)]);
        await manager.WaitForStartupCompletionAsync();
        var statuses = await manager.ListStatusesAsync();
        Assert.True(manager.Tools.Count > 0, CreateDiagnosticMessage(server, statuses));
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(manager.Tools));

        await tool.InvokeAsync(new AIFunctionArguments());

        Assert.Empty(server.DiscoverRequests);
        Assert.Equal(1, server.InitializeCount);
        Assert.Single(server.ToolsListRequests);
        Assert.Single(server.ToolCallRequests);
        Assert.Equal("initialize", ReadRpcMethod(server.Requests[0]));
        using (var initialize = JsonDocument.Parse(server.InitializeRequests[0].Body))
        {
            Assert.Equal(
                "2025-06-18",
                initialize.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString());
        }
        Assert.Equal("session-1", server.ToolsListRequests[0].SessionId);
        Assert.Equal("session-1", server.ToolCallRequests[0].SessionId);
    }

    [Fact]
    public async Task ToolCall_StaleSessionBody404_StartsNewSessionAndRetriesOnce()
    {
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options
        {
            ExpireFirstToolCall = true,
            BareStaleToolCall404 = false
        });
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([CreateServerConfig(server)]);
        await manager.WaitForStartupCompletionAsync();
        var statuses = await manager.ListStatusesAsync();
        Assert.True(manager.Tools.Count > 0, CreateDiagnosticMessage(server, statuses));
        var tool = Assert.Single(manager.Tools);
        Assert.IsAssignableFrom<AIFunction>(tool);

        var result = await ((AIFunction)tool).InvokeAsync(new AIFunctionArguments());

        Assert.NotNull(result);
        Assert.Equal(2, server.InitializeCount);
        Assert.Equal(2, server.ToolsListCount);
        Assert.Equal(2, server.ToolsCallCount);
        Assert.All(server.InitializeRequests, request => Assert.False(request.Headers.ContainsKey("Mcp-Session-Id")));
        Assert.Contains(server.ToolCallRequests, request => request.SessionId == "session-1");
        Assert.Contains(server.ToolCallRequests, request => request.SessionId == "session-2");
    }

    [Fact]
    public async Task ToolCall_Bare404AfterKnownSession_StartsNewSessionAndRetriesOnce()
    {
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options
        {
            ExpireFirstToolCall = true,
            BareStaleToolCall404 = true
        });
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([CreateServerConfig(server)]);
        await manager.WaitForStartupCompletionAsync();
        var statuses = await manager.ListStatusesAsync();
        Assert.True(manager.Tools.Count > 0, CreateDiagnosticMessage(server, statuses));
        var tool = Assert.Single(manager.Tools);
        Assert.IsAssignableFrom<AIFunction>(tool);

        var result = await ((AIFunction)tool).InvokeAsync(new AIFunctionArguments());

        Assert.NotNull(result);
        Assert.Equal(2, server.InitializeCount);
        Assert.Equal(2, server.ToolsCallCount);
        Assert.Contains(server.ToolCallRequests, request => request.SessionId == "session-1");
        Assert.Contains(server.ToolCallRequests, request => request.SessionId == "session-2");
    }

    [Fact]
    public async Task StartupListTools_StaleSession404_RetriesInitializeOnce()
    {
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options
        {
            ExpireFirstToolsList = true
        });
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([CreateServerConfig(server)]);
        await manager.WaitForStartupCompletionAsync();
        var statuses = await manager.ListStatusesAsync();

        Assert.True(manager.Tools.Count > 0, CreateDiagnosticMessage(server, statuses));
        Assert.Single(manager.Tools);
        Assert.Empty(server.DiscoverRequests);
        Assert.Equal(2, server.InitializeCount);
        Assert.Equal(2, server.ToolsListCount);
        Assert.All(server.InitializeRequests, request => Assert.False(request.Headers.ContainsKey("Mcp-Session-Id")));
        Assert.Contains(server.ToolsListRequests, request => request.SessionId == "session-1");
        Assert.Contains(server.ToolsListRequests, request => request.SessionId == "session-2");
    }

    private static string CreateDiagnosticMessage(
        MockStreamableHttpMcpServer server,
        IReadOnlyList<McpServerStatusSnapshot> statuses) =>
        "statuses=" + string.Join(", ", statuses.Select(status =>
            $"{status.Name}:{status.StartupState}:{status.LastError}")) +
        "; serverException=" + server.ServerException +
        "; requests=" + string.Join(" | ", server.Requests.Select(request =>
            $"{request.Method} {request.Path} session={request.SessionId ?? "<none>"} body={request.Body}"));

    private static McpServerConfig CreateServerConfig(MockStreamableHttpMcpServer server) =>
        new()
        {
            Name = "mock-http-mcp",
            Enabled = true,
            Transport = "streamableHttp",
            Url = server.Endpoint + "/mcp",
            StartupTimeoutSec = 10,
            ToolTimeoutSec = 10
        };

    private static string? ReadRpcMethod(RecordedHttpRequest request)
    {
        using var document = JsonDocument.Parse(request.Body);
        return document.RootElement.TryGetProperty("method", out var method) ? method.GetString() : null;
    }

    private sealed class MockStreamableHttpMcpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _acceptLoop;
        private readonly object _gate = new();
        private readonly Options _options;
        private string? _activeSessionId;

        private MockStreamableHttpMcpServer(TcpListener listener, string endpoint, Options options)
        {
            _listener = listener;
            Endpoint = endpoint;
            _options = options;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Endpoint { get; }

        public int InitializeCount { get; private set; }
        public int ToolsListCount { get; private set; }
        public int ToolsCallCount { get; private set; }

        public List<RecordedHttpRequest> Requests { get; } = [];
        public List<RecordedHttpRequest> DiscoverRequests { get; } = [];
        public List<RecordedHttpRequest> InitializeRequests { get; } = [];
        public List<RecordedHttpRequest> ToolsListRequests { get; } = [];
        public List<RecordedHttpRequest> ToolCallRequests { get; } = [];

        public Exception? ServerException { get; private set; }

        public static MockStreamableHttpMcpServer Start(Options options)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new MockStreamableHttpMcpServer(listener, $"http://127.0.0.1:{endpoint.Port}", options);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stop.CancelAsync();
                _listener.Stop();
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var request = await ReadRequestAsync(client.GetStream(), _stop.Token);
                    ResponseSpec response;
                    lock (_gate)
                    {
                        Requests.Add(request);
                        response = HandleRequest(request);
                    }

                    await WriteResponseAsync(client.GetStream(), response, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                ServerException = ex;
            }
        }

        private ResponseSpec HandleRequest(RecordedHttpRequest request)
        {
            if (string.Equals(request.Method, "DELETE", StringComparison.OrdinalIgnoreCase))
                return Empty(HttpStatusCode.Accepted);

            if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                return Empty(HttpStatusCode.MethodNotAllowed);

            using var document = JsonDocument.Parse(request.Body);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;

            return method switch
            {
                "server/discover" => HandleDiscover(request, root),
                "initialize" => HandleInitialize(request, root),
                "notifications/initialized" => Empty(HttpStatusCode.Accepted),
                "tools/list" => HandleToolsList(request, root),
                "tools/call" => HandleToolsCall(request, root),
                _ => JsonError(root, HttpStatusCode.BadRequest, -32601, "Method not found")
            };
        }

        private ResponseSpec HandleDiscover(RecordedHttpRequest request, JsonElement root)
        {
            DiscoverRequests.Add(request);
            if (!_options.SupportsModernDiscovery)
                return JsonError(root, HttpStatusCode.BadRequest, -32601, "Method not found");

            var body = $$"""
                {
                  "jsonrpc": "2.0",
                  "id": {{GetIdRaw(root)}},
                  "result": {
                    "type": "complete",
                    "supportedVersions": ["2026-07-28"],
                    "capabilities": { "tools": {} },
                    "ttlMs": 0,
                    "cacheScope": "private"
                  }
                }
                """;
            return Json(HttpStatusCode.OK, body);
        }

        private ResponseSpec HandleInitialize(RecordedHttpRequest request, JsonElement root)
        {
            InitializeCount++;
            InitializeRequests.Add(request);
            var sessionId = $"session-{InitializeCount}";
            _activeSessionId = sessionId;
            var id = GetIdRaw(root);
            var protocolVersion = root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("protocolVersion", out var protocolVersionElement)
                ? protocolVersionElement.GetString() ?? "2025-11-25"
                : "2025-11-25";
            var body = $$"""
                {
                  "jsonrpc": "2.0",
                  "id": {{id}},
                  "result": {
                    "protocolVersion": {{JsonSerializer.Serialize(protocolVersion)}},
                    "capabilities": { "tools": {} },
                    "serverInfo": { "name": "mock-http-mcp", "version": "1.0.0" }
                  }
                }
                """;
            return Json(HttpStatusCode.OK, body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mcp-Session-Id"] = sessionId
            });
        }

        private ResponseSpec HandleToolsList(RecordedHttpRequest request, JsonElement root)
        {
            ToolsListCount++;
            ToolsListRequests.Add(request);
            if (_options.ExpireFirstToolsList && ToolsListCount == 1)
            {
                _activeSessionId = null;
                return StaleSession(root, bare404: false);
            }

            if (!_options.SupportsModernDiscovery && !HasActiveSession(request))
                return JsonError(root, HttpStatusCode.BadRequest, -32600, "Missing or invalid session");

            var body = $$"""
                {
                  "jsonrpc": "2.0",
                  "id": {{GetIdRaw(root)}},
                  "result": {
                    "tools": [
                      {
                        "name": "demo_tool",
                        "description": "Demo tool",
                        "inputSchema": { "type": "object", "properties": {}, "additionalProperties": false }
                      }
                    ]
                  }
                }
                """;
            return Json(HttpStatusCode.OK, body);
        }

        private ResponseSpec HandleToolsCall(RecordedHttpRequest request, JsonElement root)
        {
            ToolsCallCount++;
            ToolCallRequests.Add(request);
            if (_options.ExpireFirstToolCall && ToolsCallCount == 1)
            {
                _activeSessionId = null;
                return StaleSession(root, _options.BareStaleToolCall404);
            }

            if (!_options.SupportsModernDiscovery && !HasActiveSession(request))
                return JsonError(root, HttpStatusCode.BadRequest, -32600, "Missing or invalid session");

            var body = $$"""
                {
                  "jsonrpc": "2.0",
                  "id": {{GetIdRaw(root)}},
                  "result": {
                    "content": [ { "type": "text", "text": "ok" } ],
                    "isError": false
                  }
                }
                """;
            return Json(HttpStatusCode.OK, body);
        }

        private bool HasActiveSession(RecordedHttpRequest request) =>
            !string.IsNullOrWhiteSpace(_activeSessionId) &&
            string.Equals(request.SessionId, _activeSessionId, StringComparison.Ordinal);

        private static ResponseSpec StaleSession(JsonElement root, bool bare404)
        {
            if (bare404)
                return Empty(HttpStatusCode.NotFound);

            return JsonError(root, HttpStatusCode.NotFound, -32001, "Session not found");
        }

        private static ResponseSpec JsonError(JsonElement root, HttpStatusCode statusCode, int code, string message)
        {
            var body = $$"""
                {
                  "jsonrpc": "2.0",
                  "id": {{GetIdRaw(root)}},
                  "error": { "code": {{code}}, "message": {{JsonSerializer.Serialize(message)}} }
                }
                """;
            return Json(statusCode, body);
        }

        private static ResponseSpec Json(
            HttpStatusCode statusCode,
            string body,
            Dictionary<string, string>? headers = null) =>
            new(statusCode, "application/json", body, headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private static ResponseSpec Empty(HttpStatusCode statusCode) =>
            new(statusCode, "application/json", string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private static string GetIdRaw(JsonElement root) =>
            root.TryGetProperty("id", out var id) ? id.GetRawText() : "null";

        private static async Task<RecordedHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1024];
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                    bytes.Add(buffer[i]);
                headerEnd = FindHeaderEnd(bytes);
            }

            if (headerEnd < 0)
                throw new IOException("HTTP request headers were incomplete.");

            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ', 3);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                headers[line[..separator]] = line[(separator + 1)..].Trim();
            }

            var bodyStart = headerEnd + 4;
            string body;
            if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
                transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                while (FindChunkedBodyEnd(bytes, bodyStart) < 0)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                        break;

                    for (var i = 0; i < read; i++)
                        bytes.Add(buffer[i]);
                }

                body = DecodeChunkedBody(bytes.Skip(bodyStart).ToArray());
            }
            else
            {
                var contentLength = headers.TryGetValue("Content-Length", out var rawLength) &&
                    int.TryParse(rawLength, out var parsedLength)
                        ? parsedLength
                        : 0;
                while (bytes.Count - bodyStart < contentLength)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                        break;

                    for (var i = 0; i < read; i++)
                        bytes.Add(buffer[i]);
                }

                body = contentLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(bytes.Skip(bodyStart).Take(contentLength).ToArray());
            }
            return new RecordedHttpRequest(
                requestLine.ElementAtOrDefault(0) ?? string.Empty,
                requestLine.ElementAtOrDefault(1) ?? string.Empty,
                headers,
                body);
        }

        private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' &&
                    bytes[i - 2] == '\n' &&
                    bytes[i - 1] == '\r' &&
                    bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static int FindChunkedBodyEnd(IReadOnlyList<byte> bytes, int bodyStart)
        {
            for (var i = bodyStart + 4; i < bytes.Count; i++)
            {
                if (bytes[i - 4] == '\r' &&
                    bytes[i - 3] == '\n' &&
                    bytes[i - 2] == '0' &&
                    bytes[i - 1] == '\r' &&
                    bytes[i] == '\n')
                {
                    return i - 4;
                }
            }

            return -1;
        }

        private static string DecodeChunkedBody(byte[] bytes)
        {
            var output = new List<byte>();
            var offset = 0;
            while (offset < bytes.Length)
            {
                var lineEnd = FindCrlf(bytes, offset);
                if (lineEnd < 0)
                    break;

                var line = Encoding.ASCII.GetString(bytes, offset, lineEnd - offset);
                var sizeText = line.Split(';', 2)[0].Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size))
                    break;

                offset = lineEnd + 2;
                if (size == 0)
                    break;

                if (offset + size > bytes.Length)
                    break;

                output.AddRange(bytes.Skip(offset).Take(size));
                offset += size + 2;
            }

            return Encoding.UTF8.GetString([.. output]);
        }

        private static int FindCrlf(IReadOnlyList<byte> bytes, int offset)
        {
            for (var i = offset + 1; i < bytes.Count; i++)
            {
                if (bytes[i - 1] == '\r' && bytes[i] == '\n')
                    return i - 1;
            }

            return -1;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            ResponseSpec response,
            CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(response.Body);
            var builder = new StringBuilder()
                .Append($"HTTP/1.1 {(int)response.StatusCode} {ReasonPhrase(response.StatusCode)}\r\n")
                .Append($"Content-Type: {response.ContentType}\r\n")
                .Append($"Content-Length: {body.Length}\r\n")
                .Append("Connection: close\r\n");
            foreach (var (name, value) in response.Headers)
                builder.Append(name).Append(": ").Append(value).Append("\r\n");
            builder.Append("\r\n");

            var header = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(header, cancellationToken);
            if (body.Length > 0)
                await stream.WriteAsync(body, cancellationToken);
        }

        private static string ReasonPhrase(HttpStatusCode statusCode) => statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Accepted => "Accepted",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            HttpStatusCode.NotFound => "Not Found",
            _ => statusCode.ToString()
        };

        public sealed class Options
        {
            public bool SupportsModernDiscovery { get; init; }
            public bool ExpireFirstToolCall { get; init; }
            public bool BareStaleToolCall404 { get; init; }
            public bool ExpireFirstToolsList { get; init; }
        }

        private sealed record ResponseSpec(
            HttpStatusCode StatusCode,
            string ContentType,
            string Body,
            Dictionary<string, string> Headers);
    }

    private sealed record RecordedHttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Headers,
        string Body)
    {
        public string? SessionId => Headers.TryGetValue("Mcp-Session-Id", out var value) ? value : null;
    }
}
