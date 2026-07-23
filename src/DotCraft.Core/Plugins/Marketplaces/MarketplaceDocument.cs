using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// The marketplace index document that lists a marketplace's installable plugins.
/// </summary>
internal sealed class MarketplaceDocument
{
    public string? Name { get; set; }

    [JsonPropertyName("interface")]
    public MarketplaceDocumentInterface? Interface { get; set; }

    public List<MarketplaceDocumentEntry> Plugins { get; set; } = [];
}

internal sealed class MarketplaceDocumentInterface
{
    public string? DisplayName { get; set; }
}

internal sealed class MarketplaceDocumentEntry
{
    public string? Name { get; set; }

    public MarketplaceDocumentEntrySource? Source { get; set; }

    public MarketplaceDocumentEntryPolicy? Policy { get; set; }

    public string? Category { get; set; }
}

internal sealed class MarketplaceDocumentEntrySource
{
    public string? Source { get; set; }

    public string? Path { get; set; }
}

internal sealed class MarketplaceDocumentEntryPolicy
{
    public string? Installation { get; set; }

    public string? Authentication { get; set; }
}

/// <summary>
/// Locates, reads, and validates marketplace documents inside a marketplace root.
/// </summary>
internal static class MarketplaceDocumentLoader
{
    public const string DefaultMarketplacePath = ".craft/plugins/marketplace.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Resolves the marketplace root that actually contains the document. An archive that
    /// unpacks into a single top-level directory is transparently descended into once.
    /// </summary>
    public static string? ResolveRoot(string snapshotRoot, string marketplacePath)
    {
        if (!IsSafeRelativePath(marketplacePath))
            return null;

        var relative = NormalizeRelativePath(marketplacePath);
        if (File.Exists(Path.Combine(snapshotRoot, relative)))
            return Path.GetFullPath(snapshotRoot);

        string[] children;
        try
        {
            children = Directory.GetDirectories(snapshotRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (children.Length == 1 && File.Exists(Path.Combine(children[0], relative)))
            return Path.GetFullPath(children[0]);

        return null;
    }

    /// <summary>
    /// Reads the marketplace document at <paramref name="documentPath"/>, or returns null with a reason.
    /// </summary>
    public static MarketplaceDocument? TryLoad(string documentPath, out string error)
    {
        error = string.Empty;
        try
        {
            var document = JsonSerializer.Deserialize<MarketplaceDocument>(File.ReadAllText(documentPath), JsonOptions);
            if (document == null)
                error = "Marketplace document is empty.";
            return document;
        }
        catch (JsonException ex)
        {
            error = $"Failed to parse marketplace document: {ex.Message}";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Failed to read marketplace document: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Validates that <paramref name="root"/> holds a usable marketplace and returns its name and
    /// display name. Throws <see cref="MarketplaceException"/> with a stable code when it does not.
    /// </summary>
    public static (string Name, string? DisplayName) ValidateRoot(string root, string marketplacePath)
    {
        var resolvedRoot = ResolveRoot(root, marketplacePath)
            ?? throw new MarketplaceException(
                MarketplaceErrorCodes.DocumentMissing,
                $"Marketplace source does not contain '{marketplacePath}'.");

        var documentPath = Path.Combine(resolvedRoot, NormalizeRelativePath(marketplacePath));
        var document = TryLoad(documentPath, out var error)
            ?? throw new MarketplaceException(MarketplaceErrorCodes.DocumentMissing, error);

        var name = document.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.DocumentMissing,
                "Marketplace document must declare a name.");
        }

        if (!IsSafeSegment(name))
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.DocumentMissing,
                $"Marketplace name '{name}' is not a usable directory name.");
        }

        return (name, string.IsNullOrWhiteSpace(document.Interface?.DisplayName) ? null : document.Interface!.DisplayName!.Trim());
    }

    /// <summary>Converts a marketplace-relative document path to a platform path segment.</summary>
    public static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>True when the document path stays inside the marketplace root.</summary>
    public static bool IsSafeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
            return false;
        return normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "..");
    }

    private static bool IsSafeSegment(string name) =>
        name is not ("." or "..")
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains('/', StringComparison.Ordinal)
        && !name.Contains('\\', StringComparison.Ordinal);
}
