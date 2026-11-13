namespace DotCraft.Plugins;

/// <summary>Disposing deletes the staged bundle unless Runtime has already published it.</summary>
public sealed class DotNetExtensionBuild : IDisposable
{
    private DotNetExtensionBuild(
        string? bundlePath,
        string? fingerprint,
        string? projectPluginRoot,
        string? projectWorkspaceRoot,
        bool succeeded,
        IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        BundlePath = bundlePath;
        Fingerprint = fingerprint;
        ProjectPluginRoot = projectPluginRoot;
        ProjectWorkspaceRoot = projectWorkspaceRoot;
        Succeeded = succeeded;
        Diagnostics = diagnostics;
    }

    public string? BundlePath { get; }

    public string? Fingerprint { get; }

    public string? ProjectPluginRoot { get; }

    public string? ProjectWorkspaceRoot { get; }

    public bool Succeeded { get; }

    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(BundlePath))
            return;

        try
        {
            if (Directory.Exists(BundlePath))
                Directory.Delete(BundlePath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static DotNetExtensionBuild Failed(IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(null, null, null, null, succeeded: false, diagnostics);

    public static DotNetExtensionBuild Successful(
        string bundlePath,
        string fingerprint,
        string projectPluginRoot,
        string projectWorkspaceRoot,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(bundlePath, fingerprint, projectPluginRoot, projectWorkspaceRoot, succeeded: true, diagnostics);
}

public sealed record DotNetExtensionBuildApplyResult(
    PluginRuntimeMutationOutcome Outcome,
    DotNetExtensionRuntimeInfo? Runtime,
    IReadOnlyList<PluginDiagnostic> Diagnostics);
