using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DotCraft.Agents.Remote;

public sealed class RemoteProviderTransport : IProviderHttpTransport, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ModelServiceConnection _connection;
    private readonly HttpClient _service;
    private readonly bool _ownsClient;
    private readonly ConcurrentDictionary<(string, Uri), HttpClient> _clients = new();

    public RemoteProviderTransport(ModelServiceConnection connection, HttpClient? client = null)
    {
        if (!connection.Endpoint.IsAbsoluteUri)
            throw new ArgumentException("The model service endpoint must be absolute.", nameof(connection));
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Token);
        _connection = connection with { Endpoint = new Uri(connection.Endpoint.AbsoluteUri.TrimEnd('/') + "/") };
        _ownsClient = client is null;
        _service = client ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public HttpClient CreateClient(string providerId, Uri providerEndpoint) =>
        _clients.GetOrAdd((providerId, providerEndpoint), key =>
            new HttpClient(new ForwardingHandler(this, key.Item1, key.Item2))
            {
                Timeout = Timeout.InfiniteTimeSpan
            });

    public async Task<ModelServiceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "providers");
        using var response = await _service.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await CheckServiceErrorAsync(response, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ModelServiceCatalog>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The model service returned no provider catalog.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_connection.Endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _connection.Token);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string providerId, Uri endpoint, HttpRequestMessage original, CancellationToken cancellationToken)
    {
        var target = original.RequestUri ?? throw new InvalidOperationException("A provider request requires a URI.");
        var baseUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        if (!baseUri.IsBaseOf(target))
            throw new InvalidOperationException("The provider request is outside its configured endpoint.");
        var relative = baseUri.MakeRelativeUri(target).OriginalString;
        using var request = CreateRequest(original.Method, $"providers/{Uri.EscapeDataString(providerId)}/http/{relative}");
        foreach (var header in original.Headers)
            if (ModelServiceProtocol.IsRequestHeader(header.Key))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        var context = new ModelServiceRequestContext(
            ProviderPipelineOptionsScope.Current?.Runtime.Model ?? string.Empty,
            ProviderRequestContextScope.Current?.CurrentIdentity,
            relative.Split('?')[0]);
        request.Headers.TryAddWithoutValidation(ModelServiceProtocol.ContextHeader,
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(context, Json)));
        request.Content = original.Content;
        try
        {
            var response = await _service.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await CheckServiceErrorAsync(response, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        finally
        {
            request.Content = null;
        }
    }

    private static async Task CheckServiceErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues(ModelServiceProtocol.ErrorHeader, out var codes))
            return;
        var error = await response.Content.ReadFromJsonAsync<ModelServiceError>(Json, cancellationToken).ConfigureAwait(false);
        throw new ModelServiceException(codes.First(), error?.Message ?? "The model service rejected this request.",
            (int)response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        if (_ownsClient)
            _service.Dispose();
    }

    private sealed class ForwardingHandler(RemoteProviderTransport owner, string providerId, Uri endpoint)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            owner.SendAsync(providerId, endpoint, request, cancellationToken);
    }
}
