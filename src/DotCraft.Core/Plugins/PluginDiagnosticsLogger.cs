namespace DotCraft.Plugins;

using Microsoft.Extensions.Logging;

/// <summary>
/// Writes Plugin Function diagnostics to the process error stream.
/// </summary>
public static class PluginDiagnosticsLogger
{
    /// <summary>
    /// Writes warning and error diagnostics.
    /// </summary>
    public static void Write(IEnumerable<PluginDiagnostic> diagnostics, ILogger? logger = null)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == PluginDiagnosticSeverity.Info)
                continue;

            logger?.LogWarning(
                "Plugin diagnostic {DiagnosticCode} for plugin {PluginId}, function {FunctionName}, path {DiagnosticPath}: {DiagnosticMessage}",
                diagnostic.Code,
                diagnostic.PluginId,
                diagnostic.FunctionName,
                diagnostic.Path,
                diagnostic.Message);
        }
    }
}
