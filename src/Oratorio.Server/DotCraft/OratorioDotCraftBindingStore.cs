using System.Text.Json;

namespace Oratorio.Server.DotCraft;

/// <summary>
/// Persists application principals and rebind hints by AppServer runtime identity.
/// Raw credentials are encrypted before they reach this store.
/// </summary>
public sealed class OratorioDotCraftBindingStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly object _gate = new();

    public void Save(OratorioDotCraftBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.AppServerIdentity);
        lock (_gate)
        {
            var document = LoadDocument();
            var bindings = document.Runtimes
                .Where(item => !string.Equals(item.AppServerIdentity, binding.AppServerIdentity, StringComparison.OrdinalIgnoreCase))
                .Append(binding)
                .OrderBy(item => item.AppServerIdentity, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new OratorioDotCraftBindingDocument(2, bindings), JsonOptions));
        }
    }

    public bool TryLoad(string appServerIdentity, out OratorioDotCraftBinding binding)
    {
        lock (_gate)
        {
            binding = LoadDocument().Runtimes.FirstOrDefault(item =>
                string.Equals(item.AppServerIdentity, appServerIdentity, StringComparison.OrdinalIgnoreCase))!;
            return IsValid(binding);
        }
    }

    public bool TryLoadForWorkspace(string? workspacePath, out OratorioDotCraftBinding binding)
    {
        lock (_gate)
        {
            binding = LoadDocument().Runtimes.FirstOrDefault(item =>
                SamePath(item.WorkspacePath, workspacePath))!;
            return IsValid(binding);
        }
    }

    public IReadOnlyList<OratorioDotCraftBinding> LoadAll()
    {
        lock (_gate)
        {
            return LoadDocument().Runtimes.Where(IsValid).ToArray();
        }
    }

    private OratorioDotCraftBindingDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(filePath)) return new(2, []);
            return JsonSerializer.Deserialize<OratorioDotCraftBindingDocument>(File.ReadAllText(filePath), JsonOptions)
                   ?? new OratorioDotCraftBindingDocument(2, []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new OratorioDotCraftBindingDocument(2, []);
        }
    }

    private static bool IsValid(OratorioDotCraftBinding? binding) =>
        binding is not null
        && !string.IsNullOrWhiteSpace(binding.AppServerIdentity)
        && !string.IsNullOrWhiteSpace(binding.WorkspacePath)
        && !string.IsNullOrWhiteSpace(binding.AppServerUrl)
        && !string.IsNullOrWhiteSpace(binding.AppId)
        && !string.IsNullOrWhiteSpace(binding.ProtectedCredential);

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

public sealed record OratorioDotCraftBindingDocument(
    int Version,
    IReadOnlyList<OratorioDotCraftBinding> Runtimes);

/// <summary>Durable authority for one AppServer identity. MCP bearers remain memory-only.</summary>
public sealed record OratorioDotCraftBinding(
    string AppServerIdentity,
    string WorkspacePath,
    string AppServerUrl,
    string AppId,
    string PrincipalId,
    string ProtectedCredential,
    DateTimeOffset PrincipalExpiresAt,
    string? AccountLabel,
    IReadOnlyList<OratorioBindingRebindHint>? Bindings = null,
    string? ProtectedAppServerToken = null);

public sealed record OratorioBindingRebindHint(
    string BindingId,
    string ThreadId,
    long AuthorityRevision);
