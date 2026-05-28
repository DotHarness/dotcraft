using System.Reflection;

namespace DotCraft.Common;

/// <summary>
/// Central accessor for the DotCraft application version.
/// Results are cached at class initialization so reflection runs only once per process.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Full informational version string including commit metadata, e.g. "0.0.1.0+1d845e83".
    /// Use for wire-protocol handshakes and diagnostic output where commit identity matters.
    /// </summary>
    public static string Informational { get; } = ReadInformational();

    /// <summary>
    /// Short 4-part version string, e.g. "0.0.1.0".
    /// Use for display in the welcome banner and protocol fields that expect a plain version
    /// without commit metadata.
    /// </summary>
    public static string Short { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";

    private static string ReadInformational() =>
        Assembly.GetEntryAssembly()
            ?.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
        ?? "0.0.0.0";
}
