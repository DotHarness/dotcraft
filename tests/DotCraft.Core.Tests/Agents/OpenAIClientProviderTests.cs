using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIClientProviderTests
{
    [Fact]
    public void CreateClientOptions_UsesConfiguredNetworkTimeout()
    {
        var endpoint = new Uri("https://example.test/v1");

        var options = OpenAIClientProvider.CreateClientOptions(endpoint, 240);

        Assert.Equal(endpoint, options.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(240), options.NetworkTimeout);
    }

    [Fact]
    public async Task GetOpenAIClient_ReplacesSdkUserAgent()
    {
        var (endpoint, requestTask) = await StartSingleJsonResponseServerAsync(
            """
            {
              "object": "list",
              "data": [
                {
                  "id": "gpt-test",
                  "object": "model",
                  "created": 1778544000,
                  "owned_by": "dotcraft-test"
                }
              ]
            }
            """);
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(
            ModelProviderProtocols.OpenAI,
            networkTimeoutSeconds: 5,
            endpoint: $"{endpoint}/v1");

        var models = await provider.GetOpenAIClient(runtime).GetOpenAIModelClient().GetModelsAsync();

        Assert.Single(models.Value);
        var request = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("GET /v1/models", request, StringComparison.Ordinal);
        Assert.Contains("User-Agent: DotCraft/", request, StringComparison.Ordinal);
        Assert.DoesNotContain("User-Agent: OpenAI/", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOpenAIClient_DisablesSdkRequestRetries()
    {
        await using var server = RetryProbeServer.Start();
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(
            ModelProviderProtocols.OpenAI,
            networkTimeoutSeconds: 5,
            endpoint: $"{server.Endpoint}/v1");

        await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await provider.GetOpenAIClient(runtime).GetOpenAIModelClient().GetModelsAsync();
        });

        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public void GetOpenAIClient_CacheKeyIncludesNetworkTimeout()
    {
        var provider = new OpenAIClientProvider();
        var baseRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var sameRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var differentTimeoutRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 900);

        var first = provider.GetOpenAIClient(baseRuntime);
        var same = provider.GetOpenAIClient(sameRuntime);
        var differentTimeout = provider.GetOpenAIClient(differentTimeoutRuntime);

        Assert.Same(first, same);
        Assert.NotSame(first, differentTimeout);
    }

    [Fact]
    public void GetOpenAIClient_RejectsNonOpenAIProtocol()
    {
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(ModelProviderProtocols.Anthropic);

        Assert.Throws<ArgumentException>(() => provider.GetOpenAIClient(runtime));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    public void LlmHttpCapture_IsOptIn(string? value, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable, value);

            Assert.Equal(expected, LlmHttpCapturePipelinePolicy.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void LlmHttpCapture_SanitizesSessionKeyForFileName()
    {
        var sanitized = LlmHttpCapturePipelinePolicy.SanitizeFileSegment("""thread:a\b/c*?""");

        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain('\\', sanitized);
        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain('*', sanitized);
        Assert.DoesNotContain('?', sanitized);
    }

    private static EffectiveModelRuntime Runtime(string protocol, int networkTimeoutSeconds = 600, string? endpoint = null) => new(
        ProviderId: protocol,
        Model: "model-a",
        Protocol: protocol,
        DisplayName: protocol,
        ApiKey: "sk-test",
        EndPoint: endpoint ?? (protocol == ModelProviderProtocols.Anthropic
            ? "https://api.anthropic.com"
            : "https://example.test/v1"),
        NetworkTimeoutSeconds: networkTimeoutSeconds,
        MaxOutputTokens: null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(protocol));

    private static async Task<(string Endpoint, Task<string> RequestTask)> StartSingleJsonResponseServerAsync(string json)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var requestTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read <= 0)
                        break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var body = Encoding.UTF8.GetBytes(json);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
                return request.ToString();
            }
            finally
            {
                listener.Stop();
            }
        });

        return ($"http://{IPAddress.Loopback}:{endpoint.Port}", requestTask);
    }

    private sealed class RetryProbeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _acceptLoop;
        private int _requestCount;

        private RetryProbeServer(TcpListener listener, string endpoint)
        {
            _listener = listener;
            Endpoint = endpoint;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Endpoint { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public static RetryProbeServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new RetryProbeServer(listener, $"http://{IPAddress.Loopback}:{endpoint.Port}");
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
                    var requestIndex = Interlocked.Increment(ref _requestCount);
                    await WriteResponseAsync(client, requestIndex);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task WriteResponseAsync(TcpClient client, int requestIndex)
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read <= 0)
                    break;
                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var bodyText = requestIndex == 1
                ? """{"error":{"message":"retry probe","type":"server_error"}}"""
                : """
                  {
                    "object": "list",
                    "data": [
                      {
                        "id": "gpt-test",
                        "object": "model",
                        "created": 1778544000,
                        "owned_by": "dotcraft-test"
                      }
                    ]
                  }
                  """;
            var status = requestIndex == 1
                ? "500 Internal Server Error"
                : "200 OK";
            var body = Encoding.UTF8.GetBytes(bodyText);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        }
    }
}
