namespace DotCraft.Plugins;

/// <summary>
/// Writes Plugin Function diagnostics to the process error stream.
/// </summary>
public static class PluginDiagnosticsLogger
{
    /// <summary>
    /// Writes warning and error diagnostics.
    /// </summary>
    public static void Write(IEnumerable<PluginDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == PluginDiagnosticSeverity.Info)
                continue;

            var plugin = string.IsNullOrWhiteSpace(diagnostic.PluginId)
                ? string.Empty
                : $" plugin={diagnostic.PluginId}";
            var function = string.IsNullOrWhiteSpace(diagnostic.FunctionName)
                ? string.Empty
                : $" function={diagnostic.FunctionName}";
            var path = string.IsNullOrWhiteSpace(diagnostic.Path)
                ? string.Empty
                : $" path={diagnostic.Path}";
            Console.Error.WriteLine(
                $"[PluginFunction:{diagnostic.Severity}] {diagnostic.Code}:{plugin}{function}{path} {diagnostic.Message}");
        }
    }
}
