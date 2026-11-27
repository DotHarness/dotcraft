using System.Text.Json;
using DotCraft.CLI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class OratorioMcpProxyTests
{
    [Fact]
    public void ResolveConfiguredTarget_PrefersExplicitOptionsThenEnvironment()
    {
        var env = new Dictionary<string, string?>
        {
            [OratorioMcpProxy.OratorioUrlEnvVar] = "http://oratorio:5087/",
            [OratorioMcpProxy.OratorioServiceTokenEnvVar] = "env-token"
        };

        var fromEnv = OratorioMcpProxy.ResolveConfiguredTarget(null, null, key => env.GetValueOrDefault(key));
        Assert.Equal(("http://oratorio:5087", "env-token"), fromEnv);

        var fromExplicit = OratorioMcpProxy.ResolveConfiguredTarget("http://127.0.0.1:5087/", "explicit-token", key => env.GetValueOrDefault(key));
        Assert.Equal(("http://127.0.0.1:5087", "explicit-token"), fromExplicit);

        var explicitUrlWithoutToken = OratorioMcpProxy.ResolveConfiguredTarget("http://127.0.0.1:5087/", null, key => env.GetValueOrDefault(key));
        Assert.Equal(("http://127.0.0.1:5087", null), explicitUrlWithoutToken);

        var hubFallback = OratorioMcpProxy.ResolveConfiguredTarget(null, null, _ => null);
        Assert.Equal((null, null), hubFallback);
    }

    [Fact]
    public async Task CallToolAsync_DoesNotReplayOnTransportDrop_AndReconnectsOnNextCall()
    {
        var callCount = 0;
        string? observedAuth = null;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithCallToolHandler((_, _) => ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "ok" }],
                IsError = false
            }));

        await using var server = builder.Build();
        server.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/mcp"))
            {
                observedAuth = context.Request.Headers.Authorization.ToString();
                context.Request.EnableBuffering();
                using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                context.Request.Body.Position = 0;
                if (doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "tools/call"
                    && Interlocked.Increment(ref callCount) == 1)
                {
                    context.Abort();
                    return;
                }
            }

            await next();
        });
        server.MapMcp("/mcp");

        await server.StartAsync();
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        await using var proxy = new OratorioMcpProxy(address, "test-bearer");
        await Assert.ThrowsAnyAsync<Exception>(() =>
            proxy.CallToolAsync(new CallToolRequestParams { Name = "CreateBoardTask" }, CancellationToken.None).AsTask());

        Assert.Equal(1, Volatile.Read(ref callCount));
        Assert.Equal("Bearer test-bearer", observedAuth);

        var secondResult = await proxy.CallToolAsync(new CallToolRequestParams { Name = "CreateBoardTask" }, CancellationToken.None);
        Assert.Equal(2, Volatile.Read(ref callCount));
        Assert.False(secondResult.IsError);
    }
}
