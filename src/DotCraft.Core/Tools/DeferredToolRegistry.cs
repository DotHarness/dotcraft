using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Search result returned by <see cref="DeferredToolActivationIndex.SearchAndActivate"/>.
/// </summary>
public sealed record ToolSearchResult(string Name, string Description);

public sealed record DeferredToolEntry(
    AITool Tool,
    string Source = "",
    string? Namespace = null,
    string? NamespaceDescription = null);

/// <summary>
/// Holds all deferred tool definitions and tracks which have been activated
/// by the model via <see cref="ToolSearchTool"/>. Activated tools are exposed
/// through <see cref="ActivatedToolsList"/> as a live reference that is shared
/// with <c>FunctionInvokingChatClient.AdditionalTools</c>, allowing the tool
/// invocation loop to find and execute them without rebuilding the agent.
/// </summary>
public sealed class DeferredToolActivationIndex
{
    private readonly Dictionary<string, AITool> _deferredTools;
    private readonly Dictionary<string, DeferredToolEntry> _entries;
    private readonly Dictionary<string, SearchDocument> _searchDocuments;
    private readonly Dictionary<string, double> _idf;
    private readonly double _averageDocumentLength;
    private readonly List<AITool> _activatedTools = [];
    private readonly HashSet<string> _activatedNames = [];
    private readonly object _lock = new();

    /// <summary>
    /// Initialises the registry with the given deferred tool definitions.
    /// </summary>
    public DeferredToolActivationIndex(IEnumerable<AITool> deferredTools)
        : this(deferredTools.Select(static tool => new DeferredToolEntry(tool)), DeferredToolLoadingMode.Simulated)
    {
    }

    /// <summary>
    /// Initialises the registry with the given deferred tool definitions and search metadata.
    /// </summary>
    internal DeferredToolActivationIndex(
        IEnumerable<DeferredToolEntry> deferredTools,
        DeferredToolLoadingMode mode = DeferredToolLoadingMode.Simulated)
    {
        Mode = mode;
        _entries = deferredTools
            .Select(NormalizeEntryIdentity)
            .GroupBy(GetIdentityKey, StringComparer.Ordinal)
            .Select(g => g.Last())
            .OrderBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(GetIdentityKey, StringComparer.Ordinal);
        _deferredTools = _entries.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Tool,
            StringComparer.Ordinal);
        (_searchDocuments, _idf, _averageDocumentLength) = BuildSearchIndex(_entries.Values);
    }

    private static DeferredToolEntry NormalizeEntryIdentity(DeferredToolEntry entry)
    {
        var entryNamespace = string.IsNullOrWhiteSpace(entry.Namespace) ? null : entry.Namespace.Trim();
        if (CanonicalToolIdentityMetadataResolver.TryGet(entry.Tool, out var existingName, out _))
        {
            if (!string.Equals(existingName.Namespace, entryNamespace, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Deferred entry namespace '{entryNamespace}' does not match canonical namespace '{existingName.Namespace}'.",
                    nameof(entry));
            }

            return entry with { Namespace = entryNamespace };
        }

        if (entryNamespace == null)
            return entry with { Namespace = null };

        if (entry.Tool is not AIFunction function)
            throw new ArgumentException("A namespaced deferred entry must contain a function tool.", nameof(entry));

        var canonicalName = new ToolName(entryNamespace, function.Name);
        var providerFlatName = ProviderToolProjector.Project([canonicalName])[canonicalName];
        return entry with
        {
            Tool = new DeferredNamespacedFunction(
                function,
                canonicalName,
                providerFlatName,
                entry.NamespaceDescription)
        };
    }

    internal static string GetIdentityKey(DeferredToolEntry entry) =>
        CanonicalToolIdentityMetadataResolver.TryGet(entry.Tool, out _, out var providerFlatName)
            ? providerFlatName
            : entry.Tool.Name;

    private sealed class DeferredNamespacedFunction(
        AIFunction innerFunction,
        ToolName canonicalName,
        string providerFlatName,
        string? namespaceDescription)
        : DelegatingAIFunction(innerFunction), ICanonicalToolIdentityMetadata
    {
        public override string Name => providerFlatName;

        public ToolName CanonicalToolName => canonicalName;

        public string ProviderFlatName => providerFlatName;

        public string? ToolNamespaceDescription => namespaceDescription;
    }

    /// <summary>
    /// All deferred tools keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, AITool> DeferredTools => _deferredTools;

    public IReadOnlyDictionary<string, DeferredToolEntry> Entries => _entries;

    internal DeferredToolLoadingMode Mode { get; }

    /// <summary>
    /// Live list of activated tools. Pass this directly as
    /// <c>FunctionInvokingChatClient.AdditionalTools</c> — it is read by
    /// the invocation loop on every iteration without snapshotting.
    /// </summary>
    public IList<AITool> ActivatedToolsList => _activatedTools;

    /// <summary>
    /// Returns an immutable snapshot of activated tool names.
    /// Used by <c>DynamicToolInjectionChatClient</c> to detect newly
    /// activated tools since the last LLM call.
    /// </summary>
    public IReadOnlyList<string> GetActivatedToolNames()
    {
        lock (_lock)
        {
            return _activatedNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// Activates deferred tools by exact name and returns the tools that exist in
    /// this registry. Used by provider-native tool-search flows where the model
    /// can select known names directly.
    /// </summary>
    public IReadOnlyList<ToolSearchResult> ActivateByName(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var requested = names
            .Select(static name => string.IsNullOrWhiteSpace(name) ? null : name.Trim())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0)
            return [];

        var results = new List<ToolSearchResult>(requested.Length);
        lock (_lock)
        {
            foreach (var name in requested)
            {
                if (!_deferredTools.TryGetValue(name!, out var tool))
                    continue;

                results.Add(new ToolSearchResult(name!, tool.Description ?? string.Empty));
                if (_activatedNames.Add(name!))
                    _activatedTools.Add(tool);
            }
        }

        return results;
    }

    /// <summary>
    /// Searches deferred tools by keyword and activates matching ones.
    /// Activation means the tool is added to <see cref="ActivatedToolsList"/>
    /// so the invocation loop can execute it, and noted in the name set so
    /// <c>DynamicToolInjectionChatClient</c> can inject the schema into the
    /// next LLM call.
    /// </summary>
    /// <param name="query">Case-insensitive keywords to match against tool names and descriptions.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    public IReadOnlyList<ToolSearchResult> SearchAndActivate(string query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return [];

        var terms = Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length == 0)
            return [];

        var scored = new List<(string Identity, AITool Tool, double Score)>();
        foreach (var document in _searchDocuments.Values)
        {
            var score = ScoreDocument(document, terms);
            if (score > 0)
                scored.Add((document.Identity, document.Tool, score));
        }

        scored.Sort(static (a, b) =>
        {
            var score = b.Score.CompareTo(a.Score);
            return score != 0
                ? score
                : string.Compare(a.Identity, b.Identity, StringComparison.OrdinalIgnoreCase);
        });

        var results = new List<ToolSearchResult>(Math.Min(scored.Count, maxResults));

        lock (_lock)
        {
            foreach (var (identity, tool, _) in scored.Take(maxResults))
            {
                results.Add(new ToolSearchResult(identity, tool.Description ?? string.Empty));

                if (_activatedNames.Add(identity))
                    _activatedTools.Add(tool);
            }
        }

        return results;
    }

    /// <summary>
    /// Replaces a previously activated tool entry with a wrapped version.
    /// Used by <c>DynamicToolInjectionChatClient</c> to substitute a raw
    /// <c>AIFunction</c> with a <c>HookWrappedFunction</c> in-place so that
    /// <c>FunctionInvokingChatClient.AdditionalTools</c> (which holds a direct
    /// reference to this list) transparently picks up the wrapped version.
    /// </summary>
    public void ReplaceActivatedTool(string name, AITool wrapped)
    {
        lock (_lock)
        {
            for (int i = 0; i < _activatedTools.Count; i++)
            {
                if (string.Equals(_activatedTools[i].Name, name, StringComparison.Ordinal))
                {
                    _activatedTools[i] = wrapped;
                    return;
                }
            }
        }
    }

    private double ScoreDocument(SearchDocument document, string[] terms)
    {
        const double k1 = 1.2;
        const double b = 0.75;
        var length = Math.Max(1, document.Length);
        var averageLength = Math.Max(1, _averageDocumentLength);
        var score = 0d;

        foreach (var term in terms)
        {
            if (!document.Frequencies.TryGetValue(term, out var frequency) || frequency <= 0)
                continue;

            var idf = _idf.GetValueOrDefault(term);
            var denominator = frequency + k1 * (1 - b + b * length / averageLength);
            score += idf * frequency * (k1 + 1) / denominator;
        }

        return score;
    }

    private static (
        Dictionary<string, SearchDocument> Documents,
        Dictionary<string, double> Idf,
        double AverageDocumentLength) BuildSearchIndex(IEnumerable<DeferredToolEntry> entries)
    {
        var documents = new Dictionary<string, SearchDocument>(StringComparer.Ordinal);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalLength = 0;

        foreach (var entry in entries)
        {
            var tokens = Tokenize(BuildSearchText(entry)).ToArray();
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in tokens)
                frequencies[token] = frequencies.GetValueOrDefault(token) + 1;

            foreach (var token in frequencies.Keys)
                documentFrequency[token] = documentFrequency.GetValueOrDefault(token) + 1;

            var length = Math.Max(1, tokens.Length);
            totalLength += length;
            var identity = GetIdentityKey(entry);
            documents[identity] = new SearchDocument(identity, entry.Tool, frequencies, length);
        }

        var documentCount = Math.Max(1, documents.Count);
        var idf = documentFrequency.ToDictionary(
            pair => pair.Key,
            pair => Math.Log(1 + (documentCount - pair.Value + 0.5) / (pair.Value + 0.5)),
            StringComparer.Ordinal);
        var averageLength = documents.Count == 0 ? 1d : (double)totalLength / documents.Count;
        return (documents, idf, averageLength);
    }

    private static string BuildSearchText(DeferredToolEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append(entry.Tool.Name);
        sb.Append(' ');
        sb.Append(entry.Tool.Description);
        sb.Append(' ');
        sb.Append(entry.Source);
        sb.Append(' ');
        sb.Append(entry.Namespace);
        sb.Append(' ');
        sb.Append(entry.NamespaceDescription);
        sb.Append(' ');
        try
        {
            if (entry.Tool is AIFunction function && function.JsonSchema.ValueKind != JsonValueKind.Undefined)
                sb.Append(function.JsonSchema.GetRawText());
        }
        catch (InvalidOperationException)
        {
        }

        return sb.ToString();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        char previous = '\0';
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (token.Length > 0 && char.IsUpper(c) && char.IsLower(previous))
                {
                    yield return token.ToString();
                    token.Clear();
                }

                token.Append(char.ToLowerInvariant(c));
                previous = c;
                continue;
            }

            if (token.Length > 0)
            {
                yield return token.ToString();
                token.Clear();
            }

            previous = '\0';
        }

        if (token.Length > 0)
            yield return token.ToString();
    }

    private sealed record SearchDocument(
        string Identity,
        AITool Tool,
        Dictionary<string, int> Frequencies,
        int Length);
}
