using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using DotCraft.Sdk;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Oratorio.Tests;

public sealed partial class OratorioAppBindingSdkTests
{
    [Fact]
    public async Task ApproveBinding_FailedActivationRevokesAuthorityAndDoesNotPersistHint()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"oratorio-app-binding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        try
        {
            var workspacePath = Path.GetFullPath(stateDirectory);
            var runtimeIdentity = $"local:{workspacePath}";
            var store = new OratorioDotCraftBindingStore(Path.Combine(stateDirectory, "binding.json"));
            store.Save(new OratorioDotCraftBinding(
                runtimeIdentity,
                workspacePath,
                "ws://127.0.0.1:9100/ws",
                "com.dotharness.oratorio",
                "principal-1",
                "credential-1",
                DateTimeOffset.UtcNow.AddDays(20),
                "Oratorio",
                []));
            var mcpRuntime = new OratorioBindingMcpRuntime();
            var service = new OratorioAppBindingService(
                new SingleClientFactory(new DotCraftAppServerClient(sdkClient)),
                null!,
                store,
                new PassthroughSecretProtector(),
                mcpRuntime,
                new OratorioBoardSurfaceRuntime(),
                NullLogger<OratorioAppBindingService>.Instance);

            var approveTask = service.ApproveAsync(
                BuildHandoff("bind", "bind_req_failed", workspacePath, runtimeIdentity),
                "http://127.0.0.1:5199",
                CancellationToken.None);

            using (var authenticate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/authenticate", authenticate.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(authenticate, new { });
            }

            using (var inspect = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/binding/request/get", inspect.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(inspect, new
                {
                    bindingRequestId = "bind_req_failed",
                    bindingId = "binding-failed",
                    threadId = "thread-1",
                    appId = "com.dotharness.oratorio",
                    state = "connecting",
                    expiresAt = "2026-07-16T12:00:00+00:00"
                });
            }

            using (var activate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/binding/activate", activate.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(activate, new
                {
                    bindingId = "binding-failed",
                    threadId = "thread-1",
                    appId = "com.dotharness.oratorio",
                    state = "failed",
                    failureReason = "mcpStartupFailed",
                    authorityRevision = 1
                });
            }

            var error = await Assert.ThrowsAsync<OratorioApiException>(async () =>
                await approveTask.WaitAsync(Timeout));
            Assert.Contains("failed", error.Message, StringComparison.Ordinal);
            Assert.Contains("mcpStartupFailed", error.Message, StringComparison.Ordinal);
            Assert.False(mcpRuntime.HasAuthority("binding-failed", 1));
            Assert.True(store.TryLoad(runtimeIdentity, out var persisted));
            Assert.Empty(persisted.Bindings ?? []);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RebindPersisted_RemovesHintsMissingFromAuthoritativeList()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"oratorio-app-binding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        try
        {
            var workspacePath = Path.GetFullPath(stateDirectory);
            var runtimeIdentity = $"local:{workspacePath}";
            var store = new OratorioDotCraftBindingStore(Path.Combine(stateDirectory, "binding.json"));
            store.Save(new OratorioDotCraftBinding(
                runtimeIdentity,
                workspacePath,
                "ws://127.0.0.1:9100/ws",
                "com.dotharness.oratorio",
                "principal-1",
                "credential-1",
                DateTimeOffset.UtcNow.AddDays(20),
                "Oratorio",
                [new OratorioBindingRebindHint("stale-binding", "deleted-thread", 4)]));
            var mcpRuntime = new OratorioBindingMcpRuntime();
            _ = mcpRuntime.Issue("stale-binding", 4);
            var service = new OratorioAppBindingService(
                new SingleClientFactory(new DotCraftAppServerClient(sdkClient)),
                null!,
                store,
                new PassthroughSecretProtector(),
                mcpRuntime,
                new OratorioBoardSurfaceRuntime(),
                NullLogger<OratorioAppBindingService>.Instance);

            var rebindTask = service.RebindPersistedAsync("http://127.0.0.1:5199", CancellationToken.None);

            using (var authenticate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/authenticate", authenticate.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(authenticate, new { });
            }

            using (var list = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/bindings/list", list.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(list, new { bindings = Array.Empty<object>() });
            }

            await rebindTask.WaitAsync(Timeout);
            Assert.True(store.TryLoad(runtimeIdentity, out var reconciled));
            Assert.Empty(reconciled.Bindings ?? []);
            Assert.False(mcpRuntime.HasAuthority("stale-binding", 4));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
