using System.Security.Cryptography;
using System.Text;

namespace Oratorio.Server.DotCraft;

/// <summary>Owns the process-local authority and endpoint contract for the published board surface.</summary>
public sealed class OratorioBoardSurfaceRuntime
{
    public const string SurfaceId = "board";
    public const string SurfacePath = "/dotcraft/surfaces/board/api/v1";

    private readonly string _bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Gets the process-local bearer published to DotCraft.</summary>
    public string Bearer => _bearer;

    /// <summary>Builds the board surface endpoint from a live loopback server base URL.</summary>
    public string BuildEndpoint(string surfaceBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(surfaceBaseUrl) ||
            !Uri.TryCreate(surfaceBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !uri.IsLoopback)
        {
            throw new ArgumentException("Oratorio must expose a loopback HTTP endpoint for the board surface.", nameof(surfaceBaseUrl));
        }

        return $"{uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')}{SurfacePath}";
    }

    /// <summary>Checks an HTTP Authorization header against the process-local bearer.</summary>
    public bool Authorize(string authorization)
    {
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = authorization[prefix.Length..].Trim();
        return candidate.Length > 0 && FixedTimeEquals(_bearer, candidate);
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
