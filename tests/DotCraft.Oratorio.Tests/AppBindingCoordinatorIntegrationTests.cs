using DotCraft.AppBinding;
using DotCraft.Mcp;
using DotCraft.Oratorio.Integrations;
using DotCraft.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Oratorio.Tests;

public sealed class AppBindingCoordinatorIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-oratorio-binding-{Guid.NewGuid():N}");
    private string CraftPath => Path.Combine(_root, ".craft");

    [Fact]
    public async Task Activation_accepts_binding_runtime_name_after_thread_composition()
    {
        var gate = new McpStartupGate();
        await using var app = new TestOratorioApp(services =>
            services.AddSingleton<IStartupFilter>(new McpStartupGateFilter(gate)));
        app.UseKestrel(0);
        using var httpClient = app.CreateClient();

        var control = new AppBindingService();
        var connection = control.StartConnection(CraftPath, "com.dotharness.oratorio", "user");
        var connected = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = connection.ConnectionRequestId,
            RequestToken = connection.RequestToken
        });
        var enabled = control.Enable(CraftPath, "thread-1", "com.dotharness.oratorio", "user");
        var authority = app.Services.GetRequiredService<OratorioBindingMcpRuntime>();
        var bearer = authority.Issue(enabled.BindingId, 0);
        await using var threadRuntime = new TestThreadMcpRuntime();
        var coordinator = new AppBindingCoordinator(control);

        var activation = coordinator.ActivateAsync(
            CraftPath,
            connected.Principal.PrincipalId,
            new AppBindingActivateCommand
            {
                BindingRequestId = enabled.BindingRequestId,
                Endpoint = new Uri(httpClient.BaseAddress!, $"/dotcraft/bindings/{enabled.BindingId}/mcp").ToString(),
                Bearer = bearer
            },
            threadRuntime,
            CancellationToken.None);

        try
        {
            var first = await Task.WhenAny(gate.RequestStarted, activation).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(gate.RequestStarted, first);
            Assert.False(activation.IsCompleted);
            Assert.Equal(
                AppBindingStates.Syncing,
                control.ListThreadBindings(CraftPath, "thread-1").Single().State);

            gate.Release();
            var result = await activation.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AppBindingStates.Active, result.State);
            Assert.Equal(4, result.ApprovedTools.Count);
            var manager = await threadRuntime.GetEffectiveMcpRuntimeAsync("thread-1");
            Assert.NotNull(manager);
            Assert.Contains(
                await manager.ListStatusesAsync(),
                status => status.Origin.IsBinding
                          && status.StartupState == "ready");
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestThreadMcpRuntime : IThreadMcpRuntimeService, IAsyncDisposable
    {
        private McpClientManager? _manager;

        public Task<McpClientManager?> GetEffectiveMcpRuntimeAsync(
            string threadId,
            CancellationToken cancellationToken = default) => Task.FromResult(_manager);

        public async Task SetBindingMcpServersAsync(
            string threadId,
            string bindingId,
            IReadOnlyList<McpServerConfig> servers,
            CancellationToken cancellationToken = default)
        {
            if (_manager is not null)
                await _manager.DisposeAsync();
            _manager = null;

            if (servers.Count == 0)
                return;

            var manager = new McpClientManager();
            var effectiveServers = McpServerComposition.Compose(
                threadId,
                threadServers: [],
                inheritedServers: [],
                bindingServers: servers);
            await manager.ConnectAsync(effectiveServers, cancellationToken);
            await manager.WaitForStartupCompletionAsync(cancellationToken);
            _manager = manager;
        }

        public async ValueTask DisposeAsync()
        {
            if (_manager is not null)
                await _manager.DisposeAsync();
        }
    }

    private sealed class McpStartupGate
    {
        private readonly TaskCompletionSource _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _used;

        public Task RequestStarted => _requestStarted.Task;

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.Value?.EndsWith("/mcp", StringComparison.Ordinal) == true
                && Interlocked.Exchange(ref _used, 1) == 0)
            {
                _requestStarted.TrySetResult();
                await _release.Task.WaitAsync(context.RequestAborted);
            }

            await next(context);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class McpStartupGateFilter(McpStartupGate gate) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(gate.InvokeAsync);
            next(app);
        };
    }
}
