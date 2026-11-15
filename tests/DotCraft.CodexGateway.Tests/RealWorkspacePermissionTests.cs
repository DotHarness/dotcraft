using System.Text.Json;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class RealWorkspacePermissionTests
{
    [Fact]
    public async Task PhonePermissionSwitchesPersistAndControlRealToolApprovals()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        var source = fixture.Source;
        await using var phone = new RealGatewayPeer(fixture.State, source);
        await phone.RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex-mobile-fixture", version = "1" },
            capabilities = new { experimentalApi = true }
        }, ct);
        await WriteApprovalAsync(phone, source.Root, ":danger-full-access", null, "danger-full-access", ct);
        var automatic = await phone.RequestAsync("config/read", new { cwd = source.Root }, ct);
        Assert.Equal("never", automatic.GetProperty("config").GetProperty("approval_policy").GetString());
        Assert.Equal(":danger-full-access", automatic.GetProperty("config").GetProperty("default_permissions").GetString());
        Assert.Equal("autoApprove", PersistedPolicy(source));

        var created = await phone.RequestAsync("thread/start", new { cwd = source.Root, sandbox = "read-only" }, ct);
        var thread = created.GetProperty("thread").GetProperty("id").GetString()!;
        Assert.Equal("never", created.GetProperty("approvalPolicy").GetString());
        Assert.Equal("dangerFullAccess", created.GetProperty("sandbox").GetProperty("type").GetString());
        await StartApprovalTurnAsync(phone, thread, null, ct);
        await CompletedAsync(phone, thread, ct);
        Assert.Equal(1, source.Model.ApprovalTool.Executions);
        Assert.Empty(phone.ServerRequests);

        var manual = await phone.RequestAsync("thread/resume", new
        {
            threadId = thread, sandbox = "workspace-write", approvalPolicy = "on-request"
        }, ct);
        Assert.Equal("on-request", manual.GetProperty("approvalPolicy").GetString());
        Assert.Equal("dotcraft", manual.GetProperty("activePermissionProfile").GetProperty("id").GetString());
        await StartApprovalTurnAsync(phone, thread, null, ct);
        var declined = await phone.NextRequestAsync("item/fileChange/requestApproval", ct);
        Assert.Equal(thread, declined.GetProperty("params").GetProperty("threadId").GetString());
        Assert.Equal(1, source.Model.ApprovalTool.Executions);
        await phone.AnswerAsync(declined, new { decision = "decline" }, ct);
        await CompletedAsync(phone, thread, ct);
        Assert.Equal(1, source.Model.ApprovalTool.Executions);

        source.Requests.Clear();
        await StartApprovalTurnAsync(phone, thread, "never", ct);
        await CompletedAsync(phone, thread, ct);
        Assert.Equal(2, source.Model.ApprovalTool.Executions);
        Assert.Single(phone.ServerRequests);
        var automaticTurn = Assert.Single(source.Requests, request => request.Method == "turn/start");
        Assert.Equal("autoApprove", automaticTurn.Parameters.GetProperty("approvalPolicy").GetString());
        Assert.DoesNotContain(source.Requests, request => request.Method == "thread/config/update");

        await StartApprovalTurnAsync(phone, thread, "on-request", ct);
        var accepted = await phone.NextRequestAsync("item/fileChange/requestApproval", ct);
        Assert.Equal(2, source.Model.ApprovalTool.Executions);
        await phone.AnswerAsync(accepted, new { decision = "accept" }, ct);
        await CompletedAsync(phone, thread, ct);
        Assert.Equal(3, source.Model.ApprovalTool.Executions);

        await WriteApprovalAsync(phone, source.Root, "dotcraft", "on-request", "read-only", ct);
        var configuration = await phone.RequestAsync("config/read", new { cwd = source.Root }, ct);
        Assert.Equal("on-request", configuration.GetProperty("config").GetProperty("approval_policy").GetString());
        Assert.Equal("dotcraft", configuration.GetProperty("config").GetProperty("default_permissions").GetString());
        Assert.Equal("default", PersistedPolicy(source));
        var next = await phone.RequestAsync("thread/start", new { cwd = source.Root, sandbox = "read-only" }, ct);
        Assert.Equal("on-request", next.GetProperty("approvalPolicy").GetString());
        var selected = await phone.RequestAsync("thread/start", new
        {
            cwd = source.Root, sandbox = "read-only", approvalPolicy = "never"
        }, ct);
        Assert.Equal("never", selected.GetProperty("approvalPolicy").GetString());
        Assert.Equal(":danger-full-access", selected.GetProperty("activePermissionProfile").GetProperty("id").GetString());
    }

    private static Task<JsonElement> StartApprovalTurnAsync(
        RealGatewayPeer phone, string thread, string? policy, CancellationToken ct) =>
        phone.RequestAsync("turn/start", new
        {
            threadId = thread, approvalPolicy = policy,
            sandboxPolicy = new { type = "readOnly", networkAccess = false },
            input = new[] { new { type = "text", text = "approval" } }
        }, ct);

    private static async Task CompletedAsync(RealGatewayPeer phone, string thread, CancellationToken ct)
    {
        var completed = await phone.NextNotificationAsync("turn/completed", thread, ct);
        Assert.Equal("completed", completed.GetProperty("turn").GetProperty("status").GetString());
    }

    private static async Task WriteApprovalAsync(
        RealGatewayPeer phone, string cwd, string profile, string? policy, string sandbox, CancellationToken ct)
    {
        var configuration = await phone.RequestAsync("config/read", new { cwd, includeLayers = true }, ct);
        var layer = Assert.Single(configuration.GetProperty("layers").EnumerateArray());
        var folder = layer.GetProperty("name").GetProperty("dotCodexFolder").GetString()!;
        var result = await phone.RequestAsync("config/batchWrite", new
        {
            filePath = Path.Combine(folder, "config.toml"), expectedVersion = layer.GetProperty("version").GetString(),
            edits = new[]
            {
                new { keyPath = "default_permissions", value = (object?)profile, mergeStrategy = "replace" },
                new { keyPath = "approval_policy", value = (object?)policy, mergeStrategy = "replace" },
                new { keyPath = "sandbox_mode", value = (object?)sandbox, mergeStrategy = "replace" }
            }
        }, ct);
        Assert.Equal("ok", result.GetProperty("status").GetString());
    }

    private static string? PersistedPolicy(RealWorkspaceAppServer source)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(source.CraftPath, "config.json")));
        return document.RootElement.GetProperty("Permissions").GetProperty("DefaultApprovalPolicy").GetString();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gateway-permissions-" + Guid.NewGuid().ToString("N"));
        private Fixture() => Source = new(Path.Combine(_root, "workspace"), "permissions");
        public RealWorkspaceAppServer Source { get; }
        public string State => Path.Combine(_root, "gateway");

        public static async Task<Fixture> StartAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            await fixture.Source.StartAsync(ct);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
