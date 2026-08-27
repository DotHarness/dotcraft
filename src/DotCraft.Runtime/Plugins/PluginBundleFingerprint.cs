using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Computes the content identity of a plugin bundle without following filesystem links.</summary>
internal static class PluginBundleFingerprint
{
    public static string Compute(string root) => PluginBundleTree.Fingerprint(root);
}

/// <summary>Applies the bundle identity rules to the shared bounded plugin tree processor.</summary>
internal static class PluginBundleTree
{
    private const string MarkerFileName = ".builtin";
    private static readonly byte[] FingerprintPrefix = "DotCraft.PluginBundleFingerprint\0v1\0"u8.ToArray();

    public static string Fingerprint(string sourceRoot) =>
        PluginContentTree.Fingerprint(sourceRoot, FingerprintPrefix, IncludeFile);

    public static string CopyAndFingerprint(string sourceRoot, string destinationRoot) =>
        PluginContentTree.CopyAndFingerprint(sourceRoot, destinationRoot, FingerprintPrefix, IncludeFile);

    private static bool IncludeFile(string normalizedPath) =>
        !string.Equals(normalizedPath, MarkerFileName, StringComparison.Ordinal);
}
