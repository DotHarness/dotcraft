using System.Net;
using System.Net.Sockets;

namespace DotCraft.Tools.Web;

internal static class WebHttp
{
    public const int MaxUrlLength = 2048;

    private const int MaxRedirects = 5;

    private const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_2) AppleWebKit/537.36";

    public static HttpClient SharedClient { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = ConnectAsync
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = IsProxyConnection(context)
            ? await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken)
            : await ResolvePublicAsync(endpoint.Host, cancellationToken);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsProxyConnection(SocketsHttpConnectionContext context)
    {
        var request = context.InitialRequestMessage;
        var uri = request.RequestUri;
        return request.Method == HttpMethod.Connect
               || (uri is not null
                   && (context.DnsEndPoint.Port != uri.Port
                   || !string.Equals(TrimBrackets(context.DnsEndPoint.Host), TrimBrackets(uri.IdnHost), StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<IPAddress[]> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(TrimBrackets(host), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0)
            throw new WebToolException($"Host '{host}' did not resolve to any address.");
        var blocked = addresses.FirstOrDefault(a => !PublicIpAddress.IsPublic(a));
        if (blocked is not null)
            throw new WebToolException($"Host '{host}' resolves to non-public address {blocked}.");
        return addresses;
    }

    public static async Task<string?> ValidateUrlAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri)
            return "URL must be absolute.";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return $"Only http and https URLs are allowed, got '{uri.Scheme}'.";
        if (uri.OriginalString.Length > MaxUrlLength || uri.AbsoluteUri.Length > MaxUrlLength)
            return $"URL exceeds {MaxUrlLength} characters.";
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "URLs with credentials are not allowed.";
        if (string.IsNullOrWhiteSpace(uri.Host))
            return "URL is missing a host.";

        var host = TrimBrackets(uri.IdnHost);
        if (IPAddress.TryParse(host, out var literal))
            return PublicIpAddress.IsPublic(literal) ? null : $"Non-public address {literal} is not allowed.";

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return null;
        }

        var blocked = addresses.FirstOrDefault(a => !PublicIpAddress.IsPublic(a));
        return blocked is null ? null : $"Host '{host}' resolves to non-public address {blocked}.";
    }

    public static async Task<(HttpResponseMessage Response, Uri FinalUri)> GetAsync(
        HttpClient client,
        Uri uri,
        Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        for (var hop = 0; ; hop++)
        {
            var error = await ValidateUrlAsync(uri, cancellationToken);
            if (error is not null)
                throw new WebToolException(hop == 0 ? error : $"Redirect to {uri} rejected: {error}");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            configure?.Invoke(request);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
                return (response, uri);

            response.Dispose();
            if (hop == MaxRedirects)
                throw new WebToolException($"Stopped after {MaxRedirects} redirects.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static string TrimBrackets(string host) => host.TrimStart('[').TrimEnd(']');
}
