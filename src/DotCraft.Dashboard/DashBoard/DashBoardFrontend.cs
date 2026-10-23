using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DotCraft.DashBoard;

public static partial class DashBoardFrontend
{
    private const string AssetPrefix = "dashboard/assets/";

    private static readonly Dictionary<string, string> ResourceNames = typeof(DashBoardFrontend).Assembly
        .GetManifestResourceNames()
        .ToDictionary(name => name.Replace('\\', '/'), StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, byte[]> Assets = new(StringComparer.Ordinal);
    private static string? _cachedHtml;
    private static string? _cachedLoginHtml;

    public static string GetHtml() => _cachedHtml ??= LoadText("dashboard/index.html");

    public static string GetLoginHtml() => _cachedLoginHtml ??= InlineAssets(LoadText("DotCraft.Resources.DashBoardLogin.html"));

    public static bool TryGetAsset(string path, out byte[] content, out string contentType)
    {
        contentType = Path.GetExtension(path) switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => ""
        };
        if (contentType.Length == 0 || !ResourceNames.TryGetValue(AssetPrefix + path, out var name))
        {
            content = [];
            return false;
        }

        content = Assets.GetOrAdd(name, LoadBytes);
        return true;
    }

    // The login page is served before sign-in, when the asset routes still require auth.
    private static string InlineAssets(string html) =>
        StylesheetLink()
            .Replace(html, match => $"<style>{LoadText(AssetPrefix + match.Groups[1].Value)}</style>")
            .Replace("/dashboard/assets/dotcraft.svg", "data:image/svg+xml;base64," + Convert.ToBase64String(LoadBytes(AssetPrefix + "dotcraft.svg")), StringComparison.Ordinal);

    [GeneratedRegex("""<link rel="stylesheet" href="/dashboard/assets/([^"]+)">""")]
    private static partial Regex StylesheetLink();

    private static string LoadText(string name)
    {
        using var reader = new StreamReader(Open(ResourceNames.GetValueOrDefault(name, name)));
        return reader.ReadToEnd();
    }

    private static byte[] LoadBytes(string name)
    {
        using var stream = Open(name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Stream Open(string name) =>
        typeof(DashBoardFrontend).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
}
