using Microsoft.AspNetCore.Builder;

namespace DotCraft.Channels;

/// <summary>
/// Implemented by channel services that bind an HTTP/HTTPS endpoint.
/// When two channels share the same <see cref="ListenScheme"/>://<see cref="ListenHost"/>:<see cref="ListenPort"/>,
/// The application host may merge matching addresses into one Kestrel server.
/// </summary>
public interface IWebHostingChannel
{
    /// <summary>
    /// URI scheme for the listener: "http" or "https".
    /// </summary>
    string ListenScheme => "http";

    /// <summary>
    /// Host address this channel binds to (e.g. "127.0.0.1" or "0.0.0.0").
    /// </summary>
    string ListenHost { get; }

    /// <summary>
    /// Port this channel binds to.
    /// </summary>
    int ListenPort { get; }

    /// <summary>
    /// Called during the registration phase so the channel can add DI services to
    /// the shared <see cref="WebApplicationBuilder"/> before the host builds it.
    /// </summary>
    void ConfigureBuilder(WebApplicationBuilder builder);

    /// <summary>
    /// Called after the host builds the shared application so the channel can register
    /// middleware and map routes onto the shared <see cref="WebApplication"/>.
    /// </summary>
    void ConfigureApp(WebApplication app);
}
