using System.Text.Json;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Turns an activation failure into the stable blocker a client explains it from.</summary>
internal static class PluginActivationBlockers
{
    /// <summary>The blocker code for an activation attempt that ran plugin code and failed.</summary>
    public const string ActivationFailed = "PluginActivationFailed";

    /// <summary>The blocker code for an activation attempt that exceeded its budget.</summary>
    public const string ActivationTimeout = "PluginActivationTimeout";

    /// <summary>The diagnostic code for a teardown that reported errors.</summary>
    public const string CleanupFailed = "PluginCleanupFailed";

    /// <summary>The diagnostic code for a teardown that outlived its deadline.</summary>
    public const string DrainTimeout = "PluginDrainTimeout";

    public static PluginRuntimeBlocker Describe(
        Exception exception,
        PluginAcceptedSnapshot snapshot,
        bool deterministic,
        string message)
    {
        if (exception is PluginServiceBindingException serviceBinding)
            return Create(serviceBinding.Code, message, serviceBinding.Parameters);

        if (deterministic)
        {
            return Create(
                PluginDotnetDiagnosticCodes.EntryAssemblyInvalid,
                message,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["assemblyPath"] = snapshot.Manifest.Dotnet!.EntryAssembly,
                    ["reason"] = "runtimeLoadFailed"
                });
        }

        return Create(
            ActivationFailed,
            message,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["phase"] = "activation"
            });
    }

    public static PluginRuntimeBlocker Create(
        string code,
        string message,
        IReadOnlyDictionary<string, object?> parameters) =>
        new(
            code,
            message,
            parameters.ToDictionary(
                static pair => pair.Key,
                static pair => JsonSerializer.SerializeToElement(pair.Value),
                StringComparer.Ordinal));
}
