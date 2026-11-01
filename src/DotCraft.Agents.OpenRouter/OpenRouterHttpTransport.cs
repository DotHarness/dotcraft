using System.Net.Http.Headers;
using DotCraft.Configuration;

namespace DotCraft.Agents;

internal sealed class OpenRouterHttpTransport(EffectiveModelRuntime runtime,
    string apiKey, IProviderHttpTransport? remote) : IProviderHttpTransport
{
    public HttpClient CreateClient(string providerId, Uri endpoint)
    {
        var upstream = runtime.IsRemote
            ? (remote ?? throw new InvalidOperationException("Remote model transport is not configured.")).CreateClient(providerId, endpoint)
            : new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return new HttpClient(new Handler(upstream, apiKey)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class Handler(HttpClient upstream, string key) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var outgoing = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers)
                if (!header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    outgoing.Headers.TryAddWithoutValidation(header.Key, header.Value);
            outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            outgoing.Headers.TryAddWithoutValidation("X-OpenRouter-Metadata", "enabled");
            if (ProviderRequestContextScope.Current?.CurrentIdentity is { } identity)
                outgoing.Headers.TryAddWithoutValidation("x-session-id", identity.RootThreadId);
            try { return await upstream.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            finally { outgoing.Content = null; }
        }

        protected override void Dispose(bool disposing) { if (disposing) upstream.Dispose(); base.Dispose(disposing); }
    }
}
