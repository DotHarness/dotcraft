using System.Reflection;

namespace DotCraft.Plugins;

/// <summary>The Host identity a .NET plugin bundle is validated against by the metadata preflight.</summary>
internal sealed record PluginHostVersion(Version Product)
{
    /// <summary>The simple name of the assembly that carries the plugin API surface.</summary>
    public const string PluginApiAssemblyName = "DotCraft.Core";

    private static readonly Lazy<PluginHostVersion> Lazy = new(Resolve, isThreadSafe: true);

    /// <summary>Gets the Host identity of the running process, resolved from the entry assembly.</summary>
    public static PluginHostVersion Current => Lazy.Value;

    /// <summary>Determines whether this Host satisfies a manifest's declared minimum Host version.
    /// An unparsable minimum is rejected by manifest admission, not here.</summary>
    public bool Satisfies(string? minHostVersion) =>
        !TryParseCanonical(minHostVersion, out var required) || Product >= required;

    /// <summary>Renders the product version in canonical <c>MAJOR.MINOR.PATCH</c> form.</summary>
    public string ProductText => $"{Product.Major}.{Product.Minor}.{Product.Build}";

    /// <summary>Parses a canonical <c>MAJOR.MINOR.PATCH</c> version. The input is not trimmed.</summary>
    public static bool TryParseCanonical(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!PluginDotnetManifestAdmission.IsCanonicalVersion(value))
            return false;

        var parts = value!.Split('.');
        version = new Version(
            int.Parse(parts[0]),
            int.Parse(parts[1]),
            int.Parse(parts[2]));
        return true;
    }

    private static PluginHostVersion Resolve() => new(ResolveProduct());

    private static Version ResolveProduct()
    {
        var entry = Assembly.GetEntryAssembly();
        var informational = entry
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (TryReadLeadingVersion(informational, out var fromInformational))
            return fromInformational;

        var assemblyVersion = entry?.GetName().Version;
        return assemblyVersion == null
            ? new Version(0, 0, 0)
            : new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0));
    }

    private static bool TryReadLeadingVersion(string? informational, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(informational))
            return false;

        // Strip build metadata ("+sha") and any prerelease suffix ("-preview.1").
        var numeric = informational.Split('+', 2)[0].Split('-', 2)[0].Trim();
        var parts = numeric.Split('.');
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = new Version(major, minor, patch);
        return true;
    }
}
