namespace DotCraft.Plugins;

/// <summary>
/// Severity for Plugin Function discovery and registration diagnostics.
/// </summary>
public enum PluginDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Describes a non-fatal Plugin Function discovery or registration issue.
/// </summary>
public sealed record PluginDiagnostic
{
    public PluginDiagnosticSeverity Severity { get; init; } = PluginDiagnosticSeverity.Warning;

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? PluginId { get; init; }

    public string? FunctionName { get; init; }

    public string? Path { get; init; }

    public static PluginDiagnostic Info(string code, string message, string? pluginId = null, string? functionName = null, string? path = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Info,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path
        };

    public static PluginDiagnostic Warning(string code, string message, string? pluginId = null, string? functionName = null, string? path = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Warning,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path
        };

    public static PluginDiagnostic Error(string code, string message, string? pluginId = null, string? functionName = null, string? path = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path
        };
}

/// <summary>
/// Stores the most recent Plugin Function discovery and registration diagnostics.
/// </summary>
public sealed class PluginDiagnosticsStore
{
    private readonly Lock _lock = new();
    private IReadOnlyList<PluginDiagnostic> _diagnostics = [];

    public static PluginDiagnosticsStore Shared { get; } = new();

    /// <summary>
    /// Returns a snapshot of the currently stored diagnostics.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> Snapshot()
    {
        lock (_lock)
        {
            return _diagnostics.ToArray();
        }
    }

    /// <summary>
    /// Replaces the current diagnostics with a new collection.
    /// </summary>
    public void Replace(IEnumerable<PluginDiagnostic> diagnostics)
    {
        lock (_lock)
        {
            _diagnostics = diagnostics.ToArray();
        }
    }

    /// <summary>
    /// Appends diagnostics to the current collection.
    /// </summary>
    public void Append(IEnumerable<PluginDiagnostic> diagnostics)
    {
        lock (_lock)
        {
            _diagnostics = _diagnostics.Concat(diagnostics).ToArray();
        }
    }
}
