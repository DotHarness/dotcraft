using System.Text.Json;

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

    /// <summary>Structured values a client composes its own message from. <see cref="Code"/> plus these are the stable contract.</summary>
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public static PluginDiagnostic Info(
        string code,
        string message,
        string? pluginId = null,
        string? functionName = null,
        string? path = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Info,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path,
            Parameters = parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        };

    public static PluginDiagnostic Warning(
        string code,
        string message,
        string? pluginId = null,
        string? functionName = null,
        string? path = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Warning,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path,
            Parameters = parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        };

    public static PluginDiagnostic Error(
        string code,
        string message,
        string? pluginId = null,
        string? functionName = null,
        string? path = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new()
        {
            Severity = PluginDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            PluginId = pluginId,
            FunctionName = functionName,
            Path = path,
            Parameters = parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        };
}
