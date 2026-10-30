using System.Collections.Frozen;
using System.Reflection;

namespace DotCraft.Runtime;

/// <summary>Which assemblies a plugin load context shares with the Host instead of loading from the bundle (spec §3).</summary>
internal static class PluginHostAssemblies
{
    private const string ProductAssemblyPrefix = "DotCraft";

    private static readonly FrozenSet<string> SharedPackages = new[]
    {
        "DiffPlex",
        "HtmlAgilityPack",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Microsoft.Data.Sqlite",
        "ModelContextProtocol",
        "ModelContextProtocol.Core",
        "ReverseMarkdown",
        "System.Management.Automation",
        "TimeZoneConverter",
        "YamlDotNet"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> FrameworkAssemblies = BuildFrameworkAssemblies();

    public static IReadOnlySet<string> SharedPackageAssemblies => SharedPackages;

    /// <summary>Determines whether an assembly simple name resolves to the Host's instance rather than the bundle copy.</summary>
    public static bool IsShared(string? simpleName) =>
        !string.IsNullOrEmpty(simpleName)
        && (simpleName.StartsWith(ProductAssemblyPrefix, StringComparison.OrdinalIgnoreCase)
            || SharedPackages.Contains(simpleName));

    internal static FrozenSet<string> ValidateHostShared(IEnumerable<string> simpleNames)
    {
        var names = simpleNames.ToArray();
        foreach (var name in names)
        {
            if (!IsSimpleName(name) || IsShared(name) || IsFrameworkAssembly(name))
            {
                throw new ArgumentException(
                    $"'{name}' is not an assembly simple name the Host can additionally share with plugins.",
                    nameof(simpleNames));
            }
        }

        return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSimpleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        try
        {
            return new AssemblyName(name).Name == name;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether an assembly simple name belongs to the shared framework the Host runs on. Read from
    /// the framework directory, not the trusted platform assembly list, which also carries the app's packages.
    /// </summary>
    public static bool IsFrameworkAssembly(string? simpleName) =>
        !string.IsNullOrEmpty(simpleName) && FrameworkAssemblies.Contains(simpleName);

    private static FrozenSet<string> BuildFrameworkAssemblies()
    {
        var location = typeof(object).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            var directory = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                return Directory.EnumerateFiles(directory, "*.dll")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(static name => !string.IsNullOrEmpty(name))
                    .Select(static name => name!)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        return TrustedPlatformAssemblies();
    }

    private static FrozenSet<string> TrustedPlatformAssemblies() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(Path.GetFileNameWithoutExtension)
        .Where(static name => !string.IsNullOrEmpty(name))
        .Select(static name => name!)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a shared assembly from the default load context, ignoring the requested version.</summary>
    internal static Assembly? TryResolveShared(string simpleName)
    {
        foreach (var loaded in System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        try
        {
            // Not loaded yet: probe the default context so a lazily used Host dependency keeps one identity.
            return System.Runtime.Loader.AssemblyLoadContext.Default
                .LoadFromAssemblyName(new AssemblyName(simpleName));
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                              or FileLoadException
                                              or BadImageFormatException)
        {
            return null;
        }
    }
}
