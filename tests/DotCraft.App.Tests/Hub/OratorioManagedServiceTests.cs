using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Hub;
using Xunit;
using Xunit.Abstractions;

namespace DotCraft.Tests.Hub;

public sealed class OratorioManagedServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Ensure_StartsIndependentOratorioWithUsableApi()
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
            var service = await registry.EnsureAsync(new EnsureManagedServiceRequest { ServiceId = "oratorio" }, timeout.Token);
            Assert.Equal(HubManagedServiceStates.Running, service.State);
            Assert.NotEqual(Environment.ProcessId, service.Pid);
            using var process = Process.GetProcessById(service.Pid!.Value);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", service.AccessToken);
            using var status = await http.GetAsync(service.Endpoint + "/api/v1/status", timeout.Token);
            status.EnsureSuccessStatusCode();

            await registry.DisposeAsync();
            Assert.True(process.HasExited);
        }
        catch (HubProtocolException ex)
        {
            output.WriteLine(JsonSerializer.Serialize(ex.Details));
            throw;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
