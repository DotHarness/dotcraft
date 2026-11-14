using System.Text.Json;
using DotCraft.CodexGateway.Protocol;
using DotCraft.CodexGateway.Workspace;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceDiscoveryTests
{
    [Fact]
    public async Task RemovingProjectCancelsPendingConnectionWithoutEndingTheRemoteSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "gateway-" + Guid.NewGuid().ToString("N"));
        var registrations = new List<CodexWorkspaceRegistration>
        {
            new(Path.Combine(root, "workspace"), "Workspace", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        };
        var catalog = new CodexWorkspaceCatalog(() => registrations.ToArray(), root);
        var connecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new List<JsonElement>();
        try
        {
            await using var session = new CodexGatewaySession(new() { StateDirectory = root }, catalog,
                () => new("installation", "gateway", null, "connected"),
                (message, _) => { messages.Add(message.Clone()); return Task.CompletedTask; },
                connectWorkspace: async (_, _, _, ct) =>
                {
                    connecting.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    throw new InvalidOperationException("Canceled connection completed.");
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ct = deadline.Token;
            await session.ReceiveAsync(JsonSerializer.SerializeToElement(new
            {
                id = 1, method = "initialize", @params = new { clientInfo = new { name = "mobile", version = "1" } }
            }), ct);
            var pending = session.ReceiveAsync(JsonSerializer.SerializeToElement(new
            {
                id = 2, method = "thread/start", @params = new { }
            }), ct);
            await connecting.Task.WaitAsync(ct);
            registrations.Clear();
            catalog.Refresh();
            await pending.WaitAsync(ct);
            Assert.Equal(-32602, messages[^1].GetProperty("error").GetProperty("code").GetInt32());
            await session.ReceiveAsync(JsonSerializer.SerializeToElement(new
            {
                id = 3, method = "project/list", @params = new { }
            }), ct);
            Assert.Empty(messages[^1].GetProperty("result").GetProperty("data").EnumerateArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WorkspaceResource_ExposesAuthorizedProjectsAndPersistsSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "gateway-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        var state = Path.Combine(root, "host");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            var registered = new List<CodexWorkspaceRegistration>
            {
                new(first, "First", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)),
                new(second, "Second", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(2))
            };
            var catalog = new CodexWorkspaceCatalog(() => registered.ToArray(), state);
            var messages = new List<JsonElement>();
            await using var session = new CodexGatewaySession(
                new() { StateDirectory = state }, catalog,
                () => new("installation", "gateway", "environment", "connected"),
                (message, _) => { messages.Add(message.Clone()); return Task.CompletedTask; },
                connectWorkspace: (_, _, _, _) => throw new InvalidOperationException("Discovery must not connect a source."));
            async Task<JsonElement> Request(int id, string method, object parameters)
            {
                await session.ReceiveAsync(JsonSerializer.SerializeToElement(new { id, method, @params = parameters }), default);
                return messages[^1];
            }
            var hello = await Request(1, "initialize", new { clientInfo = new { name = "mobile", version = "1" } });
            var home = hello.GetProperty("result").GetProperty("codexHome").GetString()!;
            var path = Path.Combine(home, ".codex-global-state.json");
            var resource = await Request(2, "fs/readFile", new { path });
            var data = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(
                resource.GetProperty("result").GetProperty("dataBase64").GetString()!));
            Assert.Equal(new[] { first, second }, data.GetProperty("electron-saved-workspace-roots").EnumerateArray().Select(p => p.GetString()));
            var selected = new Dictionary<string, object>
            {
                ["active-workspace-roots"] = new[] { second },
                ["selected-project"] = new { type = "local", projectId = catalog.Workspaces[1].Id }
            };
            await Request(3, "fs/writeFile", new { path, dataBase64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(selected)) });
            var restoredCatalog = new CodexWorkspaceCatalog(() => registered.ToArray(), state);
            var restored = restoredCatalog.ReadState(restoredCatalog.Workspaces);
            Assert.Equal(catalog.Workspaces[1].Id, restored["selected-project"]!["projectId"]!.GetValue<string>());
            var unrelated = await Request(4, "fs/readFile", new { path = Path.Combine(home, "auth.json") });
            Assert.Equal(-32602, unrelated.GetProperty("error").GetProperty("code").GetInt32());
            var projects = await Request(5, "project/list", new { sortKey = "recencyAt" });
            Assert.Equal("Second", projects.GetProperty("result").GetProperty("data")[0].GetProperty("name").GetString());
            var secondId = catalog.Workspaces[1].Id;
            var project = await Request(6, "project/read", new { projectId = secondId });
            Assert.Equal(registered[1].LastOpenedAt.ToUnixTimeSeconds(),
                project.GetProperty("result").GetProperty("project").GetProperty("recencyAt").GetInt64());

            registered.RemoveAt(1);
            resource = await Request(7, "fs/readFile", new { path });
            data = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(
                resource.GetProperty("result").GetProperty("dataBase64").GetString()!));
            Assert.Equal(catalog.Workspaces[0].Id, data.GetProperty("selected-project").GetProperty("projectId").GetString());
            Assert.Single(data.GetProperty("electron-saved-workspace-roots").EnumerateArray());
            registered.Add(new(second, "Renamed", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(3)));
            project = await Request(8, "project/read", new { projectId = secondId });
            Assert.Equal("Renamed", project.GetProperty("result").GetProperty("project").GetProperty("name").GetString());

            registered.Clear();
            foreach (var method in new[] { "project/list", "thread/list", "model/list" })
            {
                var empty = await Request(9, method, new { });
                Assert.Empty(empty.GetProperty("result").GetProperty("data").EnumerateArray());
            }
            var config = await Request(10, "config/read", new { includeLayers = true });
            Assert.Equal(JsonValueKind.Null, config.GetProperty("result").GetProperty("config").GetProperty("model").ValueKind);
            Assert.Empty(config.GetProperty("result").GetProperty("layers").EnumerateArray());
            var missing = await Request(11, "thread/start", new { });
            Assert.Equal("projectRequired", missing.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
            resource = await Request(12, "fs/readFile", new { path });
            data = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(
                resource.GetProperty("result").GetProperty("dataBase64").GetString()!));
            Assert.Empty(data.GetProperty("active-workspace-roots").EnumerateArray());
            Assert.False(data.TryGetProperty("selected-project", out _));
        }
        finally { Directory.Delete(root, true); }
    }
}
