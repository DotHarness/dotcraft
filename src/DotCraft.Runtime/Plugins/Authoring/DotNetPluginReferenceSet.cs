using System.Globalization;
using System.Xml.Linq;
using DotCraft.Plugins;
using Microsoft.CodeAnalysis;

namespace DotCraft.Runtime;

/// <summary>Provides the Host-matched metadata references used to author managed plugins.</summary>
internal sealed class DotNetPluginReferenceSet
{
    private const string CoreAssemblyName = "DotCraft.Core";
    private const string AgentsAssemblyName = "DotCraft.Agents";

    private DotNetPluginReferenceSet(IReadOnlyList<MetadataReference> references)
    {
        References = references;
    }

    /// <summary>Gets the references exposed to the plugin authoring compiler.</summary>
    public IReadOnlyList<MetadataReference> References { get; }

    /// <summary>Loads the reference pack shipped beside the current extracted Host.</summary>
    public static DotNetPluginReferenceSet LoadCurrentHost()
    {
        var hostApiAssemblyPath = typeof(IDotCraftPlugin).Assembly.Location;
        if (string.IsNullOrWhiteSpace(hostApiAssemblyPath))
            throw new InvalidOperationException("The current Host assembly has no extracted location.");

        return Load(hostApiAssemblyPath);
    }

    /// <summary>Loads the reference pack beside a Host API assembly.</summary>
    internal static DotNetPluginReferenceSet Load(string hostApiAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApiAssemblyPath);

        var hostRoot = Path.GetDirectoryName(Path.GetFullPath(hostApiAssemblyPath))
            ?? throw new InvalidOperationException("The Host assembly has no containing directory.");
        var referencesRoot = Path.Combine(hostRoot, "refs");
        if (!Directory.Exists(referencesRoot))
            throw new DirectoryNotFoundException("The DotCraft authoring reference pack is missing.");

        RequireFile(hostRoot, CoreAssemblyName, ".dll");
        RequireFile(hostRoot, AgentsAssemblyName, ".dll");
        RequireFile(hostRoot, CoreAssemblyName, ".xml");
        RequireFile(hostRoot, AgentsAssemblyName, ".xml");

        var references = Directory.EnumerateFiles(referencesRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(static path => IsBclReference(Path.GetFileNameWithoutExtension(path)))
            .Concat(Directory.EnumerateFiles(hostRoot, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(IsHostContractReference))
            .OrderBy(static path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .Select(CreateReference)
            .ToArray();

        return new DotNetPluginReferenceSet(Array.AsReadOnly(references));
    }

    private static bool IsHostContractReference(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return IsPluginApiAssembly(name);
    }

    internal static bool IsPluginApiAssembly(string assemblyName) =>
        string.Equals(assemblyName, CoreAssemblyName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(assemblyName, AgentsAssemblyName, StringComparison.OrdinalIgnoreCase)
        || PluginHostAssemblies.SharedPackageAssemblies.Contains(assemblyName);

    private static bool IsBclReference(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.Win32", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Microsoft.CSharp", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "netstandard", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase);

    private static MetadataReference CreateReference(string assemblyPath)
    {
        var documentationPath = Path.ChangeExtension(assemblyPath, ".xml");
        var documentation = File.Exists(documentationPath)
            ? new FileDocumentationProvider(documentationPath)
            : null;

        return MetadataReference.CreateFromFile(assemblyPath, documentation: documentation);
    }

    private static void RequireFile(string root, string assemblyName, string extension)
    {
        if (!File.Exists(Path.Combine(root, assemblyName + extension)))
            throw new FileNotFoundException(
                $"The DotCraft authoring reference pack is missing {assemblyName}{extension}.");
    }

    private sealed class FileDocumentationProvider : DocumentationProvider
    {
        private readonly string _path;
        private readonly Lazy<IReadOnlyDictionary<string, string>> _documentation;

        public FileDocumentationProvider(string path)
        {
            _path = path;
            _documentation = new Lazy<IReadOnlyDictionary<string, string>>(
                () => LoadDocumentation(path),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private static IReadOnlyDictionary<string, string> LoadDocumentation(string path) =>
            XDocument.Load(path)
                .Descendants("member")
                .ToDictionary(
                    static member => (string)member.Attribute("name")!,
                    static member => string.Concat(member.Nodes()
                        .Select(static node => node.ToString(SaveOptions.DisableFormatting))),
                    StringComparer.Ordinal);

        protected override string? GetDocumentationForSymbol(
            string documentationMemberID,
            CultureInfo preferredCulture,
            CancellationToken cancellationToken = default) =>
            _documentation.Value.GetValueOrDefault(documentationMemberID);

        public override bool Equals(object? obj) =>
            obj is FileDocumentationProvider other
            && string.Equals(_path, other._path, StringComparison.Ordinal);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_path);
    }
}
