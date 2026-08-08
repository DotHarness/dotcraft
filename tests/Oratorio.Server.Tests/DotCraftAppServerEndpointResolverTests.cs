using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oratorio.Server.DotCraft;

namespace Oratorio.Server.Tests;

public sealed class DotCraftAppServerEndpointResolverTests
{
    [Fact]
    public async Task ResolveAsync_PrefersRunningHubWorkspaceAppServer()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("oratorio-hub-lock-");
        var lockPath = Path.Combine(tempDirectory.FullName, "hub.lock");
        await File.WriteAllTextAsync(lockPath, $$"""
            {
              "pid": {{Environment.ProcessId}},
              "apiBaseUrl": "http://127.0.0.1:49123",
              "token": "hub-token"
            }
            """);

        try
        {
            var handler = new CaptureHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/v1/status")
                {
                    Assert.Null(request.Headers.Authorization);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{ "status": "ok" }""")
                    };
                }

                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("hub-token", request.Headers.Authorization?.Parameter);
                Assert.Equal("/v1/appservers/by-workspace", request.RequestUri?.AbsolutePath);
                Assert.Contains("path=%2Fworkspace%2Fsample", request.RequestUri?.Query);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "workspacePath": "/workspace/sample",
                          "canonicalWorkspacePath": "/workspace/sample",
                          "state": "running",
                          "pid": 1234,
                          "endpoints": {
                            "appServerWebSocket": "ws://127.0.0.1:53697/ws?token=from-hub"
                          },
                          "serviceStatus": {},
                          "serverVersion": "test",
                          "startedByHub": true,
                          "exitCode": null,
                          "lastError": null,
                          "recentStderr": null
                        }
                        """)
                };
            });
            var resolver = new DotCraftAppServerEndpointResolver(
                new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
                {
                    AppServerUrl = "ws://127.0.0.1:9100/ws",
                    HubLockPath = lockPath
                }),
                new SingleClientFactory(new HttpClient(handler)),
                new PassthroughSecretProtector(),
                NullLogger<DotCraftAppServerEndpointResolver>.Instance);

            var endpoint = await resolver.ResolveAsync("/workspace/sample", CancellationToken.None);

            Assert.NotNull(endpoint);
            Assert.Equal("hub", endpoint.Source);
            Assert.Equal("ws://127.0.0.1:53697/ws?token=from-hub", endpoint.Url);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EnsureAvailableAsync_AsksHubToEnsureWorkspaceAppServer()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("oratorio-hub-lock-");
        var lockPath = Path.Combine(tempDirectory.FullName, "hub.lock");
        await File.WriteAllTextAsync(lockPath, $$"""
            {
              "pid": {{Environment.ProcessId}},
              "apiBaseUrl": "http://127.0.0.1:49124",
              "token": "hub-token"
            }
            """);

        try
        {
            var handler = new CaptureHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/v1/status")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{ "status": "ok" }""")
                    };
                }

                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("hub-token", request.Headers.Authorization?.Parameter);
                Assert.Equal("/v1/appservers/ensure", request.RequestUri?.AbsolutePath);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "workspacePath": "/workspace/sample",
                          "canonicalWorkspacePath": "/workspace/sample",
                          "state": "running",
                          "pid": 1234,
                          "endpoints": {
                            "appServerWebSocket": "ws://127.0.0.1:53698/ws?token=ensured"
                          },
                          "serviceStatus": {},
                          "serverVersion": "test",
                          "startedByHub": true,
                          "exitCode": null,
                          "lastError": null,
                          "recentStderr": null
                        }
                        """)
                };
            });
            var processManager = new DotCraftAppServerProcessManager(
                new NullEndpointResolver(),
                new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
                {
                    HubLockPath = lockPath
                }),
                new SingleClientFactory(new HttpClient(handler)),
                NullLogger<DotCraftAppServerProcessManager>.Instance);

            var endpoint = await processManager.EnsureAvailableAsync("/workspace/sample", CancellationToken.None);

            Assert.Equal("hub", endpoint.Source);
            Assert.Equal("ws://127.0.0.1:53698/ws?token=ensured", endpoint.Url);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_UsesConfiguredEndpointWhenHubDiscoveryIsUnavailable()
    {
        var resolver = new DotCraftAppServerEndpointResolver(
            new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                AppServerUrl = "ws://127.0.0.1:9100/ws",
                HubDiscoveryEnabled = false
            }),
            new SingleClientFactory(new HttpClient(new CaptureHandler(_ => throw new InvalidOperationException("Hub should not be queried.")))),
            new PassthroughSecretProtector(),
            NullLogger<DotCraftAppServerEndpointResolver>.Instance);

        var defaultEndpoint = await resolver.ResolveAsync("/workspace/sample", CancellationToken.None);
        var repositoryEndpoint = await resolver.ResolveAsync("/workspace/other", CancellationToken.None);

        Assert.NotNull(defaultEndpoint);
        Assert.Equal("configuration", defaultEndpoint.Source);
        Assert.Equal("ws://127.0.0.1:9100/ws", defaultEndpoint.Url);
        Assert.NotNull(repositoryEndpoint);
        Assert.Equal("configuration", repositoryEndpoint.Source);
        Assert.Equal("ws://127.0.0.1:9100/ws", repositoryEndpoint.Url);
    }

    [Fact]
    public async Task ProbeAsync_ReportsHubReasonForNonDefaultWorkspaceWithoutEndpoint()
    {
        var processManager = new DotCraftAppServerProcessManager(
            new NullEndpointResolver(),
            new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
            {
                AppServerUrl = "ws://127.0.0.1:9100/ws"
            }),
            new SingleClientFactory(new HttpClient(new CaptureHandler(_ => throw new InvalidOperationException("Hub should not be queried.")))),
            NullLogger<DotCraftAppServerProcessManager>.Instance);

        var probe = await processManager.ProbeAsync("/workspace/other", CancellationToken.None);

        Assert.False(probe.Connected);
        Assert.Equal("workspaceNotRegisteredInHub", probe.Reason);
        Assert.Contains("Hub", probe.Message);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NullEndpointResolver : IDotCraftAppServerEndpointResolver
    {
        public Task<DotCraftAppServerEndpoint?> ResolveAsync(string workspacePath, CancellationToken ct) =>
            Task.FromResult<DotCraftAppServerEndpoint?>(null);

        public string? ResolveConfiguredToken() => null;
    }

    private sealed class PassthroughSecretProtector : Oratorio.Server.Services.IConfigurationSecretProtector
    {
        public bool IsProtected(string? value) => false;
        public string Protect(string value) => value;
        public string? Unprotect(string? value) => value;
    }
}
