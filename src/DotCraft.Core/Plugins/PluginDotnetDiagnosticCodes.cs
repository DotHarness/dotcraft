namespace DotCraft.Plugins;

/// <summary>Stable diagnostic codes emitted while admitting and preflighting a <c>dotnet</c> plugin.
/// The code is the localization key clients compose their own message from.</summary>
public static class PluginDotnetDiagnosticCodes
{
    /// <summary>A <c>dotnet</c> or <c>dependencies</c> manifest field failed static admission. Parameters: <c>field</c>, <c>reasonCode</c>, <c>dependencyId</c>.</summary>
    public const string AdmissionFailed = "PluginDotnetAdmissionFailed";

    /// <summary>The declared <c>minHostVersion</c> exceeds the running Host version. Parameters: <c>minHostVersion</c>, <c>hostVersion</c>.</summary>
    public const string HostVersionUnsatisfied = "PluginHostVersionUnsatisfied";

    /// <summary>The declared entry assembly does not exist in the bundle.</summary>
    public const string EntryAssemblyMissing = "PluginEntryAssemblyMissing";

    /// <summary>The declared entry assembly is unreadable or is not a managed assembly.</summary>
    public const string EntryAssemblyInvalid = "PluginEntryAssemblyInvalid";

    /// <summary>The entry assembly's <c>.deps.json</c> is absent, so dependencies cannot resolve.</summary>
    public const string DependencyManifestMissing = "PluginDependencyManifestMissing";

    /// <summary>The bundle targets a framework this Host does not load.</summary>
    public const string TargetFrameworkMismatch = "PluginTargetFrameworkMismatch";

    /// <summary>The declared entry type is absent or does not satisfy the entry contract. Parameters: <c>entryType</c>, <c>reason</c>.</summary>
    public const string EntryTypeInvalid = "PluginEntryTypeInvalid";

    /// <summary>A declared exported API assembly is missing, unreadable, or ambiguous. Parameters: <c>assemblyPath</c>, <c>reason</c>.</summary>
    public const string ApiExportInvalid = "PluginApiExportInvalid";

    /// <summary>The plugin has no trust grant, so no load context may be created for it. Parameters: <c>pluginId</c>, <c>trustStatus</c>, <c>fingerprintPrefix</c>.</summary>
    public const string Untrusted = "PluginUntrusted";

    /// <summary>The accepted bundle bytes changed after trust was granted. Parameters: <c>pluginId</c>, <c>trustStatus</c>, <c>fingerprintPrefix</c>, <c>trustedFingerprintPrefix</c>.</summary>
    public const string TrustModified = "PluginTrustModified";
}
