namespace DotCraft.Plugins;

/// <summary>
/// Applies model-visible Plugin Function name conflict rules.
/// </summary>
public static class PluginFunctionConflictResolver
{
    /// <summary>
    /// Drops registrations whose model-visible function name conflicts with an earlier registration or reserved tool name.
    /// </summary>
    public static IReadOnlyList<PluginFunctionRegistration> ResolveRegistrations(
        IEnumerable<PluginFunctionRegistration> registrations,
        ICollection<PluginDiagnostic> diagnostics,
        IReadOnlySet<string>? reservedToolNames = null)
    {
        var result = new List<PluginFunctionRegistration>();
        var seen = new HashSet<string>(reservedToolNames ?? new HashSet<string>(), StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            var descriptor = registration.Descriptor;
            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginFunctionName",
                    $"Plugin '{descriptor.PluginId}' produced an empty function name.",
                    descriptor.PluginId));
                continue;
            }

            if (!seen.Add(descriptor.Name))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "DuplicatePluginFunctionName",
                    $"Plugin function '{descriptor.Name}' from plugin '{descriptor.PluginId}' was skipped because the model-visible name is already registered.",
                    descriptor.PluginId,
                    descriptor.Name));
                continue;
            }

            result.Add(registration);
        }

        return result;
    }
}
