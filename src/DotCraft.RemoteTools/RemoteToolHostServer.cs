using System.Net;
using System.Security.Cryptography.X509Certificates;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal static class RemoteToolHostServer
{
    public static async Task RunAsync(
        RemoteToolHostStorage storage,
        AppConfig? config = null,
        IReadOnlyList<IToolSource>? trustedPluginSources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var state = storage.LoadHostState()
            ?? throw new InvalidOperationException(
                "Remote Tool Host is not set up. Run 'dotcraft tool-host setup' first.");
        if (!File.Exists(state.CertificatePath))
            throw new InvalidOperationException("Remote Tool Host TLS certificate is missing.");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            state.CertificatePath,
            password: null,
            OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
                : X509KeyStorageFlags.DefaultKeySet);
        foreach (var workspacePath in state.Workspaces.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!RemoteToolArtifactStore.CleanupStaleArtifacts(Path.GetFullPath(workspacePath)))
                throw new InvalidOperationException("Remote Tool Host could not clean stale workspace artifacts.");
        }
        var leases = new WorkspaceLeaseManager(
            onReleased: released => RemoteToolArtifactStore.CleanupLeaseArtifacts(
                released.WorkspacePath,
                released.LeaseId));
        var handlers = new RemoteToolHostMcpHandlers(
            storage,
            leases,
            config,
            trustedPluginSources);

        var listenUri = new Uri(state.ListenEndpoint, UriKind.Absolute);
        var listenAddress = IPAddress.TryParse(listenUri.Host, out var parsedAddress)
            ? parsedAddress
            : IPAddress.Any;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.AddConsole();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(listenAddress, listenUri.Port, endpoint => endpoint.UseHttps(certificate)));
        builder.Services.AddSingleton(handlers);
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "dotcraft.remote-tool-host",
                    Version = RemoteToolHostProtocol.ProfileVersion
                };
                options.ServerInstructions =
                    "Pure DotCraft Remote Tool Host. It exposes paired workspace execution tools only.";
                options.RequestHandlers = handlers.CreateExtensionHandlers().ToList();
            })
            .WithHttpTransport(options => options.Stateless = false)
            .WithListToolsHandler((request, ct) => handlers.ListToolsAsync(request, ct))
            .WithCallToolHandler((request, ct) => handlers.CallToolAsync(request, ct));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("Origin"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            var currentState = storage.LoadHostState();
            if (currentState is null
                || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                || !TokenUtilities.VerifyToken(authorization[7..], currentState.TokenHash))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            await next().ConfigureAwait(false);
        });
        app.MapMcp("/mcp");

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await handlers.DisposeAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            certificate.Dispose();
        }
    }
}
