using System.Text;
using System.Text.Json;

namespace DotCraft.RemoteTools;

internal sealed record HubEndpoint(Uri BaseUrl, string Token);

internal interface IHubEndpointProvider
{
    HubEndpoint? TryResolve();
}

/// <summary>
/// Managed AppServers receive the Hub endpoint through the environment; every other process reads
/// the Hub discovery file.
/// </summary>
internal sealed class HubEndpointProvider(string? craftHome = null) : IHubEndpointProvider
{
    private const string BaseUrlVariable = "DOTCRAFT_HUB_API_BASE_URL";
    private const string TokenVariable = "DOTCRAFT_HUB_TOKEN";

    private readonly string _craftHome = craftHome ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft");

    public HubEndpoint? TryResolve()
    {
        var url = Environment.GetEnvironmentVariable(BaseUrlVariable);
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        if (!string.IsNullOrWhiteSpace(url)
            && !string.IsNullOrWhiteSpace(token)
            && Uri.TryCreate(url, UriKind.Absolute, out var fromEnvironment))
        {
            return new HubEndpoint(fromEnvironment, token);
        }
        return ReadLockFile(Path.Combine(_craftHome, "hub", "hub.lock"));
    }

    private static HubEndpoint? ReadLockFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            if (!root.TryGetProperty("apiBaseUrl", out var baseUrl)
                || !root.TryGetProperty("token", out var token)
                || !Uri.TryCreate(baseUrl.GetString(), UriKind.Absolute, out var uri))
            {
                return null;
            }
            var value = token.GetString();
            return string.IsNullOrEmpty(value) ? null : new HubEndpoint(uri, value);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
