using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace DotCraft.Plugins;

/// <summary>Performs non-executing metadata validation for a structurally admitted .NET plugin bundle.</summary>
internal static class PluginDotnetMetadataInspector
{
    /// <summary>The single target framework moniker this Host loads plugin bundles for.</summary>
    public const string SupportedTargetFramework = ".NETCoreApp,Version=v10.0";

    private const string PluginApiAssemblyName = PluginHostVersion.PluginApiAssemblyName;
    private const string PluginEntryInterfaceNamespace = "DotCraft.Plugins";
    private const string PluginEntryInterfaceName = "IDotCraftPlugin";

    /// <summary>Validates entry and exported API metadata without loading or executing the inspected
    /// assembly. Any error diagnostic leaves the plugin in the catalog but not activatable.</summary>
    public static IReadOnlyList<PluginDiagnostic> Inspect(
        PluginManifest manifest,
        PluginHostVersion? host = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Dotnet == null)
            return [];

        host ??= PluginHostVersion.Current;
        var diagnostics = new List<PluginDiagnostic>();
        ValidateHostVersion(manifest, host, diagnostics);

        var entryPath = Resolve(manifest.RootPath, manifest.Dotnet.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            diagnostics.Add(Error(
                PluginDotnetDiagnosticCodes.EntryAssemblyMissing,
                $"Plugin entry assembly '{manifest.Dotnet.EntryAssembly}' is missing.",
                manifest,
                manifest.Dotnet.EntryAssembly,
                ("assemblyPath", manifest.Dotnet.EntryAssembly)));
            return diagnostics;
        }

        var dependencyManifest = ToDependencyManifestPath(manifest.Dotnet.EntryAssembly);
        if (!File.Exists(Resolve(manifest.RootPath, dependencyManifest)))
        {
            diagnostics.Add(Error(
                PluginDotnetDiagnosticCodes.DependencyManifestMissing,
                $"Plugin dependency manifest '{dependencyManifest}' is missing.",
                manifest,
                dependencyManifest,
                ("dependencyManifestPath", dependencyManifest)));
        }

        if (!TryReadAssembly(entryPath, out var entryAssembly, out var entryError))
        {
            diagnostics.Add(Error(
                PluginDotnetDiagnosticCodes.EntryAssemblyInvalid,
                $"Plugin entry assembly '{manifest.Dotnet.EntryAssembly}' is invalid: {entryError}.",
                manifest,
                manifest.Dotnet.EntryAssembly,
                ("assemblyPath", manifest.Dotnet.EntryAssembly),
                ("reason", entryError)));
            return diagnostics;
        }

        using (entryAssembly)
        {
            ValidateTargetFramework(entryAssembly.Reader, manifest, diagnostics);
            ValidateEntryType(entryAssembly.Reader, manifest, diagnostics);

            var simpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                entryAssembly.SimpleName
            };
            foreach (var exportedPath in manifest.Dotnet.ExportedApiAssemblies)
            {
                InspectExportedApi(manifest, exportedPath, simpleNames, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void InspectExportedApi(
        PluginManifest manifest,
        string exportedPath,
        HashSet<string> simpleNames,
        List<PluginDiagnostic> diagnostics)
    {
        var absolutePath = Resolve(manifest.RootPath, exportedPath);
        if (!File.Exists(absolutePath))
        {
            diagnostics.Add(ApiError(manifest, exportedPath, "assemblyMissing"));
            return;
        }

        if (!TryReadAssembly(absolutePath, out var assembly, out var error))
        {
            diagnostics.Add(ApiError(manifest, exportedPath, error));
            return;
        }

        using (assembly)
        {
            if (!simpleNames.Add(assembly.SimpleName))
                diagnostics.Add(ApiError(manifest, exportedPath, "duplicateAssemblySimpleName"));
        }
    }

    private static void ValidateHostVersion(
        PluginManifest manifest,
        PluginHostVersion host,
        List<PluginDiagnostic> diagnostics)
    {
        var minHostVersion = manifest.Dotnet!.MinHostVersion;
        if (host.Satisfies(minHostVersion))
            return;

        diagnostics.Add(Error(
            PluginDotnetDiagnosticCodes.HostVersionUnsatisfied,
            $"Plugin requires DotCraft {minHostVersion} or newer but this Host is {host.ProductText}.",
            manifest,
            manifest.Dotnet.EntryAssembly,
            ("minHostVersion", minHostVersion),
            ("hostVersion", host.ProductText)));
    }

    private static void ValidateTargetFramework(
        MetadataReader reader,
        PluginManifest manifest,
        List<PluginDiagnostic> diagnostics)
    {
        var observed = ReadTargetFramework(reader) ?? "<missing>";
        if (string.Equals(observed, SupportedTargetFramework, StringComparison.Ordinal))
            return;

        diagnostics.Add(Error(
            PluginDotnetDiagnosticCodes.TargetFrameworkMismatch,
            $"Plugin targets '{observed}' but this Host supports '{SupportedTargetFramework}'.",
            manifest,
            manifest.Dotnet!.EntryAssembly,
            ("supportedFramework", SupportedTargetFramework),
            ("observedFramework", observed)));
    }

    private static void ValidateEntryType(
        MetadataReader reader,
        PluginManifest manifest,
        List<PluginDiagnostic> diagnostics)
    {
        var entryTypeName = manifest.Dotnet!.EntryType;
        TypeDefinitionHandle? match = null;
        foreach (var handle in reader.TypeDefinitions)
        {
            if (string.Equals(GetTypeFullName(reader, handle), entryTypeName, StringComparison.Ordinal))
            {
                match = handle;
                break;
            }
        }

        if (match == null)
        {
            diagnostics.Add(EntryTypeError(manifest, "typeNotFound"));
            return;
        }

        var type = reader.GetTypeDefinition(match.Value);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
        {
            diagnostics.Add(EntryTypeError(manifest, "typeNotPublic"));
            return;
        }

        if ((type.Attributes & TypeAttributes.Interface) != 0
            || (type.Attributes & TypeAttributes.Abstract) != 0)
        {
            diagnostics.Add(EntryTypeError(manifest, "typeNotConcrete"));
            return;
        }

        if (type.GetGenericParameters().Count > 0)
        {
            diagnostics.Add(EntryTypeError(manifest, "typeIsGeneric"));
            return;
        }

        if (!ImplementsPluginEntryInterface(reader, match.Value, []))
        {
            diagnostics.Add(EntryTypeError(manifest, "entryContractMissing"));
            return;
        }

        if (!HasPublicParameterlessConstructor(reader, type))
            diagnostics.Add(EntryTypeError(manifest, "publicParameterlessConstructorMissing"));
    }

    private static bool ImplementsPluginEntryInterface(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        HashSet<TypeDefinitionHandle> visited)
    {
        if (!visited.Add(typeHandle))
            return false;
        var type = reader.GetTypeDefinition(typeHandle);
        foreach (var implementationHandle in type.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (implementation.Interface.Kind != HandleKind.TypeReference)
                continue;

            var typeReference = reader.GetTypeReference((TypeReferenceHandle)implementation.Interface);
            if (!string.Equals(reader.GetString(typeReference.Namespace), PluginEntryInterfaceNamespace, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(typeReference.Name), PluginEntryInterfaceName, StringComparison.Ordinal)
                || typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            var assemblyReference = reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope);
            if (string.Equals(reader.GetString(assemblyReference.Name), PluginApiAssemblyName, StringComparison.Ordinal))
                return true;
        }

        return type.BaseType.Kind == HandleKind.TypeDefinition
               && ImplementsPluginEntryInterface(
                   reader,
                   (TypeDefinitionHandle)type.BaseType,
                   visited);
    }

    private static bool HasPublicParameterlessConstructor(MetadataReader reader, TypeDefinition type)
    {
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(reader.GetString(method.Name), ".ctor", StringComparison.Ordinal)
                || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }

            if (!method.GetParameters().Any(parameterHandle =>
                    reader.GetParameter(parameterHandle).SequenceNumber > 0))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            return GetTypeFullName(reader, declaringType) + "+" + name;

        var @namespace = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    private static string? ReadTargetFramework(MetadataReader reader)
    {
        var assembly = reader.GetAssemblyDefinition();
        foreach (var attributeHandle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsTargetFrameworkAttribute(reader, attribute.Constructor))
                continue;

            try
            {
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                    return null;
                return blob.ReadSerializedString();
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsTargetFrameworkAttribute(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
            return false;

        var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference)
            return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return string.Equals(reader.GetString(type.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal)
               && string.Equals(reader.GetString(type.Name), "TargetFrameworkAttribute", StringComparison.Ordinal);
    }

    private static bool TryReadAssembly(
        string path,
        out InspectedAssembly assembly,
        out string error)
    {
        assembly = null!;
        error = string.Empty;
        FileStream? stream = null;
        PEReader? peReader = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                stream.Dispose();
                error = "notManagedAssembly";
                return false;
            }

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                peReader.Dispose();
                stream.Dispose();
                error = "metadataIsNotAssembly";
                return false;
            }

            var definition = reader.GetAssemblyDefinition();
            assembly = new InspectedAssembly(stream, peReader, reader, reader.GetString(definition.Name));
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            peReader?.Dispose();
            stream?.Dispose();
            error = exception is UnauthorizedAccessException ? "assemblyReadDenied" : "invalidManagedAssembly";
            return false;
        }
    }

    private static PluginDiagnostic EntryTypeError(PluginManifest manifest, string reason) =>
        Error(
            PluginDotnetDiagnosticCodes.EntryTypeInvalid,
            $"Plugin entry type '{manifest.Dotnet!.EntryType}' is invalid: {reason}.",
            manifest,
            manifest.Dotnet.EntryAssembly,
            ("entryType", manifest.Dotnet.EntryType),
            ("reason", reason));

    private static PluginDiagnostic ApiError(PluginManifest manifest, string assemblyPath, string reason) =>
        Error(
            PluginDotnetDiagnosticCodes.ApiExportInvalid,
            $"Plugin API export '{assemblyPath}' is invalid: {reason}.",
            manifest,
            assemblyPath,
            ("assemblyPath", assemblyPath),
            ("reason", reason));

    private static PluginDiagnostic Error(
        string code,
        string message,
        PluginManifest manifest,
        string? path,
        params (string Name, string? Value)[] parameters) =>
        PluginDiagnostic.Error(
            code,
            message,
            manifest.Id,
            path: path,
            parameters: ToParameters(parameters));

    private static IReadOnlyDictionary<string, JsonElement> ToParameters(
        (string Name, string? Value)[] parameters) =>
        parameters.ToDictionary(
            static pair => pair.Name,
            static pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal);

    private static string Resolve(string root, string manifestRelativePath) =>
        Path.GetFullPath(Path.Combine(root, manifestRelativePath[2..].Replace('/', Path.DirectorySeparatorChar)));

    private static string ToDependencyManifestPath(string entryAssemblyPath) =>
        entryAssemblyPath[..^4] + ".deps.json";

    private sealed class InspectedAssembly(
        Stream stream,
        PEReader peReader,
        MetadataReader reader,
        string simpleName) : IDisposable
    {
        public MetadataReader Reader { get; } = reader;

        public string SimpleName { get; } = simpleName;

        public void Dispose()
        {
            peReader.Dispose();
            stream.Dispose();
        }
    }
}
