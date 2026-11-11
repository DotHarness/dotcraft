using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed partial class ThreadPrivateMcpSessionTests
{
    [Fact]
    public async Task Refresh_BlockedStartupDoesNotBlockTurnCancellationOrArchive()
    {
        await using var server = await GatedMcpServer.StartAsync();
        var client = new BlockingReplyClient();
        await using var harness = await Harness.CreateAsync(chatClient: client);
        var thread = await harness.StartAsync();
        var turn = DrainTurnAsync();
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var turnId = Assert.Single(thread.Turns).Id;
        var existing = await harness.Workspace.ListConfigsAsync();
        var slow = new McpServerConfig
        {
            Name = "slow", Transport = "streamableHttp", Url = server.Endpoint, StartupTimeoutSec = 120
        };
        try
        {
            await harness.Workspace.ConnectAsync([.. existing, slow]);
            var refresh = harness.Sessions.RefreshMcpRuntimesAsync();
            await server.BothClientsStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

            await harness.Sessions.CancelTurnAsync(thread.Id, turnId).WaitAsync(TimeSpan.FromSeconds(15));
            await turn.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(TurnStatus.Cancelled, Assert.Single(thread.Turns).Status);
            await refresh.WaitAsync(TimeSpan.FromSeconds(15));

            await harness.Sessions.ArchiveThreadAsync(thread.Id).WaitAsync(TimeSpan.FromSeconds(15));
            await server.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(ThreadStatus.Archived, thread.Status);
            Assert.Null(await harness.Sessions.GetEffectiveMcpRuntimeAsync(thread.Id));
        }
        finally
        {
            server.Release();
        }

        await harness.Sessions.UnarchiveThreadAsync(thread.Id);
        await harness.Sessions.RefreshMcpRuntimesAsync();
        var restored = (await harness.Sessions.GetEffectiveMcpRuntimeAsync(thread.Id))!;
        Assert.Equal("ready", Assert.Single(await restored.ListStatusesAsync(), status => status.Name == slow.Name).StartupState);

        async Task DrainTurnAsync()
        {
            await foreach (var _ in harness.Sessions.SubmitInputAsync(thread.Id, "wait")) { }
        }
    }

    private sealed class BlockingReplyClient : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class GatedMcpServer(WebApplication app) : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _clientsStarted;

        public TaskCompletionSource BothClientsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RequestCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Endpoint => app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        public static async Task<GatedMcpServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var server = new GatedMcpServer(app);
            app.MapPost("/", server.HandleAsync);
            await app.StartAsync();
            return server;
        }

        private async Task HandleAsync(HttpContext context)
        {
            var request = (await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted))!.AsObject();
            if (Interlocked.Increment(ref _clientsStarted) == 2)
                BothClientsStarted.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                RequestCancelled.TrySetResult();
                throw;
            }
            var response = McpFixtureProtocol.Respond(request);
            if (response is null)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response.ToJsonString(), context.RequestAborted);
        }

        public void Release() => _release.TrySetResult();
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
