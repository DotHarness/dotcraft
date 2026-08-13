using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServerTestClient;

internal sealed class StreamRetrySmokeFaultProxy : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Host"
    };

    private readonly WebApplication _app;
    private readonly Uri _upstreamBaseUri;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<StreamRetrySmokeProxyRequestReport> _requests = new();
    private int _faultInjected;
    private int _faultedRequests;
    private int _forwardedRequests;

    private StreamRetrySmokeFaultProxy(WebApplication app, Uri endpoint, Uri upstreamBaseUri)
    {
        _app = app;
        Endpoint = endpoint;
        _upstreamBaseUri = upstreamBaseUri;
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public Uri Endpoint { get; }

    public static async Task<StreamRetrySmokeFaultProxy> StartAsync(
        Uri upstreamBaseUri,
        CancellationToken cancellationToken = default)
    {
        var port = AllocateLoopbackPort();
        var endpoint = new Uri($"http://127.0.0.1:{port}");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));

        var app = builder.Build();
        var proxy = new StreamRetrySmokeFaultProxy(app, endpoint, upstreamBaseUri);
        app.Run(proxy.HandleAsync);
        await app.StartAsync(cancellationToken);
        return proxy;
    }

    public StreamRetrySmokeProxySnapshot Snapshot() => new()
    {
        FaultedRequests = Volatile.Read(ref _faultedRequests),
        ForwardedRequests = Volatile.Read(ref _forwardedRequests),
        Requests = [.. _requests]
    };

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _app.StopAsync(cts.Token);
        }
        finally
        {
            await _app.DisposeAsync();
            _httpClient.Dispose();
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (ShouldFault(context.Request)
            && Interlocked.CompareExchange(ref _faultInjected, 1, 0) == 0)
        {
            await FaultAsync(context);
            return;
        }

        await ForwardAsync(context);
    }

    private async Task FaultAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Interlocked.Increment(ref _faultedRequests);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.Body.FlushAsync(context.RequestAborted);
            context.Abort();
        }
        finally
        {
            stopwatch.Stop();
            _requests.Enqueue(new StreamRetrySmokeProxyRequestReport
            {
                Kind = "faulted",
                Method = context.Request.Method,
                Path = SafePath(context.Request),
                DurationMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    private async Task ForwardAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new StreamRetrySmokeProxyRequestReport
        {
            Kind = "forwarded",
            Method = context.Request.Method,
            Path = SafePath(context.Request)
        };

        try
        {
            using var upstreamRequest = CreateUpstreamRequest(context.Request);
            using var upstreamResponse = await _httpClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            Interlocked.Increment(ref _forwardedRequests);
            report.UpstreamStatusCode = (int)upstreamResponse.StatusCode;

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, context.Response);
            await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            report.ErrorMessage = ex.Message;
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("stream retry smoke proxy upstream failure", context.RequestAborted);
        }
        finally
        {
            stopwatch.Stop();
            report.DurationMs = stopwatch.ElapsedMilliseconds;
            _requests.Enqueue(report);
        }
    }

    private HttpRequestMessage CreateUpstreamRequest(HttpRequest request)
    {
        var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.Method), BuildUpstreamUri(request));
        if (CanHaveBody(request))
            upstreamRequest.Content = new StreamContent(request.Body);

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
                continue;

            var values = header.Value.ToArray();
            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, values))
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, values);
        }

        return upstreamRequest;
    }

    private Uri BuildUpstreamUri(HttpRequest request)
    {
        var upstreamPath = _upstreamBaseUri.AbsolutePath.TrimEnd('/');
        if (string.Equals(upstreamPath, "/", StringComparison.Ordinal))
            upstreamPath = string.Empty;

        var requestPath = request.Path.HasValue ? request.Path.Value ?? string.Empty : string.Empty;
        var combinedPath = string.IsNullOrWhiteSpace(upstreamPath)
            ? requestPath
            : upstreamPath + "/" + requestPath.TrimStart('/');

        var builder = new UriBuilder(_upstreamBaseUri)
        {
            Path = string.IsNullOrWhiteSpace(combinedPath) ? "/" : combinedPath,
            Query = request.QueryString.HasValue
                ? request.QueryString.Value!.TrimStart('?')
                : string.Empty
        };
        return builder.Uri;
    }

    private static void CopyResponseHeaders(HttpResponseMessage upstreamResponse, HttpResponse response)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
                response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
                response.Headers[header.Key] = header.Value.ToArray();
        }

        response.Headers.Remove("transfer-encoding");
    }

    private static bool ShouldFault(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return false;

        var path = request.Path.Value ?? string.Empty;
        return path.Contains("chat/completions", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/messages", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanHaveBody(HttpRequest request) =>
        request.ContentLength is > 0
        || string.Equals(request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Method, HttpMethods.Patch, StringComparison.OrdinalIgnoreCase);

    private static string SafePath(HttpRequest request) =>
        request.Path.HasValue ? request.Path.Value ?? "/" : "/";

    private static int AllocateLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
