using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace DotCraft.Oratorio.Hosting;

public static partial class OratorioApplication
{
    private static void RegisterStartupBanner(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = ResolveDisplayAddresses(app).ToArray();
            var primaryAddress = addresses.FirstOrDefault() ?? "http://127.0.0.1:5087";
            var managedServiceId = app.Configuration[ServiceIdKey];
            if (!string.IsNullOrWhiteSpace(managedServiceId))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    type = "dotcraft.managed-service.ready",
                    serviceId = managedServiceId,
                    endpoint = primaryAddress.TrimEnd('/'),
                    version = typeof(OratorioApplication).Assembly.GetName().Version?.ToString()
                }));
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=====================================");
            Console.WriteLine(" Oratorio is running");
            Console.WriteLine("=====================================");
            Console.WriteLine($" API:      {AppendPath(primaryAddress, "/api/v1/status")}");
            Console.WriteLine($" Health:   {AppendPath(primaryAddress, "/health")}");
            Console.WriteLine(" Mode:     Headless server");
            if (addresses.Length > 1)
            {
                Console.WriteLine($" Listening: {string.Join(", ", addresses)}");
            }

            Console.WriteLine("=====================================");
            Console.WriteLine();
        });
    }

    private static IEnumerable<string> ResolveDisplayAddresses(WebApplication app)
    {
        var serverAddresses = app.Services
            .GetService<IServer>()?
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;

        var addresses = serverAddresses is { Count: > 0 } ? serverAddresses : app.Urls;
        return addresses
            .Select(NormalizeDisplayAddress)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeDisplayAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return address
                .Replace("://0.0.0.0", "://localhost", StringComparison.OrdinalIgnoreCase)
                .Replace("://[::]", "://localhost", StringComparison.OrdinalIgnoreCase)
                .Replace("://*", "://localhost", StringComparison.OrdinalIgnoreCase)
                .Replace("://+", "://localhost", StringComparison.OrdinalIgnoreCase);
        }

        var host = uri.Host is "0.0.0.0" or "::" or "*" or "+"
            ? "localhost"
            : uri.Host;
        return new UriBuilder(uri) { Host = host }.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static string AppendPath(string baseAddress, string path) =>
        $"{baseAddress.TrimEnd('/')}/{path.TrimStart('/')}";
}