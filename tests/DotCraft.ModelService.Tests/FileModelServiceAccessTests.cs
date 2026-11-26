using System.Net;
using System.Net.Http.Headers;
using DotCraft.Agents.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class FileModelServiceAccessTests : IDisposable
{
    private readonly string _state = Directory.CreateTempSubdirectory("model-service-access-").FullName;

    [Fact]
    public async Task UnavailableProviderDoesNotDiscloseItsStateToUnauthorizedCallers()
    {
        File.WriteAllText(Path.Combine(_state, "config.json"), """
            { "Providers": { "offline": { "BackendId": "fixture-backend" } } }
            """);
        var clients = new ModelServiceClientStore(_state);
        var authorized = clients.Create("allowed", ["offline", "missing"]).Token;
        var unauthorized = clients.Create("denied", ["other"]).Token;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModelServiceAccess>(_ => new FileModelServiceAccess(_state));
        builder.Services.AddDotCraftModelService();
        await using var service = builder.Build();
        service.MapDotCraftModelService();
        await service.StartAsync();
        using var client = service.GetTestClient();

        await AssertErrorAsync(authorized, "offline", HttpStatusCode.ServiceUnavailable, "backend_unavailable");
        await AssertErrorAsync(unauthorized, "offline", HttpStatusCode.Forbidden, "provider_forbidden");
        await AssertErrorAsync(authorized, "missing", HttpStatusCode.Forbidden, "provider_forbidden");

        async Task AssertErrorAsync(string token, string provider, HttpStatusCode status, string code)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/model-service/providers/{provider}/http/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, Assert.Single(response.Headers.GetValues(ModelServiceProtocol.ErrorHeader)));
        }
    }

    public void Dispose() => Directory.Delete(_state, recursive: true);
}
