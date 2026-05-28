using DotCraft.Abstractions;
using Microsoft.AspNetCore.Builder;

namespace DotCraft.Hosting;

/// <summary>
/// Manages a set of <see cref="WebApplication"/> instances keyed by (scheme, host, port).
/// When multiple <see cref="IWebHostingChannel"/> instances share the same address, they all
/// receive the same builder/app, composing their services and routes into a single Kestrel server.
/// </summary>
/// <remarks>
/// Lifecycle:
/// <list type="number">
///   <item><description>Call <see cref="Register"/> for each channel to collect builders.</description></item>
///   <item><description>Call <see cref="BuildAll"/> to materialise all <see cref="WebApplication"/> instances.</description></item>
///   <item><description>Call <see cref="ConfigureApps"/> to let every channel map its middleware and routes.</description></item>
///   <item><description>Call <see cref="StartAllAsync"/> to start all servers.</description></item>
///   <item><description>Call <see cref="StopAllAsync"/> (or <see cref="DisposeAsync"/>) on shutdown.</description></item>
/// </list>
/// </remarks>
public sealed class WebHostPool : IAsyncDisposable
{
    private record PoolKey(string Scheme, string Host, int Port);

    private readonly Dictionary<PoolKey, WebApplicationBuilder> _builders = new();
    private readonly Dictionary<PoolKey, WebApplication> _apps = new();
    private readonly List<IWebHostingChannel> _channels = [];

    /// <summary>
    /// Registers an <see cref="IWebHostingChannel"/> with the pool.
    /// If another channel already claimed the same address, the channel shares its builder.
    /// </summary>
    public WebApplicationBuilder Register(IWebHostingChannel channel)
    {
        _channels.Add(channel);
        var key = KeyFor(channel);
        if (!_builders.TryGetValue(key, out var builder))
        {
            builder = WebApplication.CreateBuilder();
            _builders[key] = builder;
        }
        channel.ConfigureBuilder(builder);
        return builder;
    }

    /// <summary>
    /// Returns the <see cref="WebApplicationBuilder"/> for the given address.
    /// Can be used to register additional services (e.g. dashboard) that are not channels.
    /// </summary>
    public WebApplicationBuilder GetOrCreateBuilder(string scheme, string host, int port)
    {
        var key = new PoolKey(scheme, host, port);
        if (!_builders.TryGetValue(key, out var builder))
        {
            builder = WebApplication.CreateBuilder();
            _builders[key] = builder;
        }
        return builder;
    }

    /// <summary>
    /// Builds all registered <see cref="WebApplicationBuilder"/> instances into <see cref="WebApplication"/> objects.
    /// Must be called after all <see cref="Register"/> / <see cref="GetOrCreateBuilder"/> calls and before
    /// <see cref="ConfigureApps"/> or <see cref="GetApp"/>.
    /// </summary>
    public void BuildAll()
    {
        foreach (var (key, builder) in _builders)
            _apps[key] = builder.Build();
    }

    /// <summary>
    /// Calls <see cref="IWebHostingChannel.ConfigureApp"/> on every registered channel,
    /// passing each channel its shared <see cref="WebApplication"/>.
    /// </summary>
    public void ConfigureApps()
    {
        foreach (var channel in _channels)
            channel.ConfigureApp(_apps[KeyFor(channel)]);
    }

    /// <summary>
    /// Returns the built <see cref="WebApplication"/> for the given address.
    /// Can be used to register additional routes (e.g. dashboard) that are not channels.
    /// </summary>
    public WebApplication GetApp(string scheme, string host, int port)
    {
        var key = new PoolKey(scheme, host, port);
        if (!_apps.TryGetValue(key, out var app))
            throw new InvalidOperationException(
                $"No web application found for {scheme}://{host}:{port}. Call BuildAll() first.");
        return app;
    }

    /// <summary>
    /// Starts all web applications. Each app's <c>RunAsync</c> is fired-and-forgotten so this
    /// method returns immediately, matching the fire-and-forget pattern used in the channel services.
    /// </summary>
    public Task StartAllAsync()
    {
        foreach (var (key, app) in _apps)
        {
            var url = $"{key.Scheme}://{key.Host}:{key.Port}";
            _ = app.RunAsync(url);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Signals all web applications to stop.
    /// </summary>
    public async Task StopAllAsync()
    {
        var tasks = _apps.Values.Select(a => a.StopAsync());
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
        foreach (var app in _apps.Values)
            await app.DisposeAsync();
    }

    private static PoolKey KeyFor(IWebHostingChannel channel)
        => new(channel.ListenScheme, channel.ListenHost, channel.ListenPort);
}
