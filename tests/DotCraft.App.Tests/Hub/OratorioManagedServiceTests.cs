using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class OratorioManagedServiceTests
{
    [Fact]
    public async Task Ensure_StartsIndependentOratorioAndRestartPreservesState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"OratorioManagedService_{Guid.NewGuid():N}");
        var paths = HubPaths.Resolve(root);
        var stateRoot = Path.Combine(paths.CraftHomePath, "oratorio");
        Directory.CreateDirectory(stateRoot);
        await File.WriteAllTextAsync(Path.Combine(stateRoot, "config.json"),
            """{"Oratorio":{"DotCraft":{"HubDiscoveryEnabled":false}}}""");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            await using var registry = new ManagedLocalServiceRegistry(ManagedLocalServiceDefinitions.CreateBuiltIns(paths));
            var request = new EnsureManagedServiceRequest { ServiceId = "oratorio" };
            var responses = await Task.WhenAll(Enumerable.Range(0, 2)
                .Select(_ => registry.EnsureAsync(request, timeout.Token)));
            var service = responses[0];
            Assert.Equal(HubManagedServiceStates.Running, service.State);
            Assert.Equal(service.Pid, responses[1].Pid);
            Assert.Equal(service.AccessToken, responses[1].AccessToken);
            Assert.NotEqual(Environment.ProcessId, service.Pid);
            using var original = Process.GetProcessById(service.Pid!.Value);

            using var unauthorized = await http.GetAsync(service.Endpoint + "/api/v1/status", timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", service.AccessToken);
            using var created = await http.PostAsJsonAsync(service.Endpoint + "/api/v1/local-tasks",
                new { title = "Persisted task" }, timeout.Token);
            created.EnsureSuccessStatusCode();
            var task = await created.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            var taskId = task.GetProperty("item").GetProperty("itemId").GetString();

            Assert.True(File.Exists(Path.Combine(stateRoot, "oratorio.db")));
            var restarted = await registry.RestartAsync(
                new ManagedServiceRequest { ServiceId = "oratorio" }, timeout.Token);
            Assert.Equal(HubManagedServiceStates.Running, restarted.State);
            Assert.NotEqual(service.AccessToken, restarted.AccessToken);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restarted.AccessToken);
            var persisted = await http.GetFromJsonAsync<JsonElement>(
                restarted.Endpoint + "/api/v1/items/id/" + taskId, timeout.Token);
            Assert.Equal("Persisted task", persisted.GetProperty("item").GetProperty("title").GetString());
            Assert.True(original.HasExited);

            using var replacement = Process.GetProcessById(restarted.Pid!.Value);
            await registry.DisposeAsync();
            await replacement.WaitForExitAsync(timeout.Token);
            Assert.True(replacement.HasExited);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
