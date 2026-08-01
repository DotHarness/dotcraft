using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.ProtocolGen;

public static class ProtocolArtifactGenerator
{
    public const string PackageRelativePath = "src/DotCraft.Protocol.Contracts/Artifacts/AppServer";
    private const string SchemaBase = "https://schemas.dotcraft.dev/appserver/v1/";

    public static IReadOnlyDictionary<string, string> Build(
        string repositoryRoot,
        ContractArtifactProfile profile = ContractArtifactProfile.Stable)
    {
        var ir = ContractIrBuilder.SelectProfile(ContractIrBuilder.Build(repositoryRoot), profile);
        return BuildContractFiles(ir);
    }

    public static IReadOnlyDictionary<string, string> BuildRepositoryArtifacts(
        string repositoryRoot,
        ContractArtifactProfile profile = ContractArtifactProfile.Stable)
    {
        var ir = ContractIrBuilder.SelectProfile(ContractIrBuilder.Build(repositoryRoot), profile);
        var contractFiles = BuildContractFiles(ir);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in contractFiles)
            files[$"{PackageRelativePath}/{pair.Key}"] = pair.Value;
        foreach (var pair in SdkBindingArtifactGenerator.Build(ir, contractFiles))
            files[pair.Key] = pair.Value;
        return files;
    }

    public static void Validate(
        string repositoryRoot,
        IReadOnlyCollection<string>? modules = null,
        ContractArtifactProfile profile = ContractArtifactProfile.Stable)
    {
        var ir = ContractIrBuilder.SelectProfile(ContractIrBuilder.Build(repositoryRoot), profile);
        if (modules is { Count: > 0 })
            ir = ContractIrBuilder.SelectModules(ir, modules);
        var contractFiles = BuildContractFiles(ir);
        _ = SdkBindingArtifactGenerator.Build(ir, contractFiles);
    }

    private static IReadOnlyDictionary<string, string> BuildContractFiles(ContractIr ir)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["appserver.manifest.json"] = CanonicalJson(BuildManifest(ir)),
            ["schemas/appserver.schema.json"] = CanonicalJson(BuildBundleSchema(ir)),
            ["openrpc.json"] = CanonicalJson(BuildOpenRpc(ir))
        };

        foreach (var type in ir.Types)
            files[type.SchemaPath] = CanonicalJson(BuildTypeSchema(type, aggregate: false));

        ValidateJsonArtifacts(files);
        files["contract.sha256"] = ComputeHash(files) + "\n";
        return files;
    }

    public static void Generate(
        string repositoryRoot,
        ContractArtifactProfile profile = ContractArtifactProfile.Stable)
    {
        var files = BuildRepositoryArtifacts(repositoryRoot, profile);
        var stagingRoot = Path.Combine(Path.GetTempPath(), "dotcraft-protocolgen", Guid.NewGuid().ToString("N"));
        try
        {
            WriteFiles(stagingRoot, files);
            InstallFiles(stagingRoot, repositoryRoot, files.Keys);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    public static IReadOnlyList<string> Check(
        string repositoryRoot,
        ContractArtifactProfile profile = ContractArtifactProfile.Stable)
    {
        var expected = BuildRepositoryArtifacts(repositoryRoot, profile);
        var drift = new List<string>();
        foreach (var pair in expected)
        {
            var path = Path.Combine(repositoryRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                drift.Add($"missing: {pair.Key}");
                continue;
            }

            var actual = File.ReadAllText(path);
            if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
                drift.Add($"changed: {pair.Key}");
        }

        foreach (var generatedRoot in GeneratedRoots())
        {
            var root = Path.Combine(repositoryRoot, generatedRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
                continue;
            var expectedUnderRoot = expected.Keys.Where(path => path.StartsWith(generatedRoot + "/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (IsIgnoredGeneratedFile(path))
                    continue;
                var relative = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
                if (!expectedUnderRoot.Contains(relative))
                    drift.Add($"extra: {relative}");
            }
        }

        return drift.Order(StringComparer.Ordinal).ToArray();
    }

    public static string ComputeHash(IReadOnlyDictionary<string, string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in files
                     .Where(static pair => pair.Key == "appserver.manifest.json" || pair.Key.StartsWith("schemas/", StringComparison.Ordinal))
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(pair.Key));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(Normalize(pair.Value)));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static JsonObject BuildManifest(ContractIr ir)
    {
        var modules = new JsonArray(ir.Modules.Select(module => (JsonNode)new JsonObject
        {
            ["name"] = module.Name,
            ["stability"] = module.Stability
        }).ToArray());

        var types = new JsonArray(ir.Types.Select(type => (JsonNode)new JsonObject
        {
            ["id"] = type.Id,
            ["name"] = type.Name,
            ["module"] = type.Module,
            ["kind"] = KindName(type.Kind),
            ["schema"] = type.SchemaPath,
            ["additionalProperties"] = type.AllowsAdditionalProperties,
            ["fields"] = new JsonArray(type.Fields.Select(field => (JsonNode)BuildManifestField(field)).ToArray()),
            ["variants"] = new JsonArray(type.Variants.Select(variant => (JsonNode)new JsonObject
            {
                ["discriminator"] = variant.Discriminator,
                ["type"] = variant.TypeId
            }).ToArray())
        }).ToArray());

        var methods = new JsonArray(ir.Methods.Select(method => (JsonNode)new JsonObject
        {
            ["name"] = method.Name,
            ["kind"] = method.Kind,
            ["direction"] = method.Direction,
            ["paramsType"] = method.ParamsType,
            ["resultType"] = method.ResultType,
            ["module"] = method.Module,
            ["capability"] = method.Capability,
            ["scope"] = method.Scope,
            ["notificationOptOut"] = method.NotificationOptOut,
            ["errors"] = new JsonArray(method.Errors.Select(static error => JsonValue.Create(error)).ToArray()),
            ["since"] = method.Since,
            ["specRef"] = method.SpecRef,
            ["stability"] = method.Stability
        }).ToArray());

        return new JsonObject
        {
            ["formatVersion"] = ir.FormatVersion,
            ["contractVersion"] = ir.ContractVersion,
            ["protocolVersion"] = ir.ProtocolVersion,
            ["modules"] = modules,
            ["types"] = types,
            ["methods"] = methods
        };
    }

    private static JsonObject BuildManifestField(ContractField field)
    {
        var value = new JsonObject
        {
            ["name"] = field.Name,
            ["type"] = field.Type.DisplayName,
            ["required"] = field.Required,
            ["nullable"] = field.Nullable,
            ["const"] = field.Constant
        };
        if (field.Type.Minimum.HasValue)
            value["minimum"] = field.Type.Minimum.Value;
        if (field.Type.Maximum.HasValue)
            value["maximum"] = field.Type.Maximum.Value;
        return value;
    }

    private static JsonObject BuildBundleSchema(ContractIr ir)
    {
        var definitions = new JsonObject();
        foreach (var type in ir.Types)
            definitions[type.Name] = BuildTypeSchema(type, aggregate: true);

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = SchemaBase + "appserver.schema.json",
            ["title"] = "DotCraft AppServer contract bundle",
            ["$defs"] = definitions
        };
    }

    private static JsonObject BuildTypeSchema(ContractType type, bool aggregate)
    {
        var schema = BuildTypeBody(type, aggregate);
        if (!aggregate)
        {
            schema.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
            schema.Insert(1, "$id", SchemaBase + $"{type.Module}/{type.Name}.schema.json");
        }
        schema.Insert(aggregate ? 0 : 2, "title", type.Name);
        return schema;
    }

    private static JsonObject BuildTypeBody(ContractType type, bool aggregate)
    {
        if (type.Kind == ContractTypeKind.Union)
        {
            var mapping = new JsonObject();
            foreach (var variant in type.Variants)
                mapping[variant.Discriminator] = RefFor(variant.TypeId, aggregate);
            return new JsonObject
            {
                ["oneOf"] = new JsonArray(type.Variants.Select(variant => (JsonNode)new JsonObject
                {
                    ["$ref"] = RefFor(variant.TypeId, aggregate)
                }).ToArray()),
                ["discriminator"] = new JsonObject
                {
                    ["propertyName"] = "type",
                    ["mapping"] = mapping
                }
            };
        }

        if (type.Kind == ContractTypeKind.Enum)
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Enum.GetNames(type.RuntimeType)
                    .Select(name => JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name))).ToArray())
            };
        }

        var properties = new JsonObject();
        foreach (var field in type.Fields)
        {
            var fieldSchema = SchemaFor(field.Type, aggregate);
            if (field.Constant is not null)
                fieldSchema["const"] = field.Constant;
            properties[field.Name] = field.Nullable ? NullableSchema(fieldSchema) : fieldSchema;
        }

        var required = type.Fields.Where(static field => field.Required).Select(static field => field.Name).Order(StringComparer.Ordinal).ToArray();
        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = type.AllowsAdditionalProperties
        };
        if (required.Length > 0)
            result["required"] = new JsonArray(required.Select(static name => JsonValue.Create(name)).ToArray());
        return result;
    }

    private static JsonObject SchemaFor(ContractTypeRef reference, bool aggregate) => reference.Kind switch
    {
        ContractTypeRefKind.String => new JsonObject { ["type"] = "string" },
        ContractTypeRefKind.Boolean => new JsonObject { ["type"] = "boolean" },
        ContractTypeRefKind.Integer => IntegerSchema(reference),
        ContractTypeRefKind.Number => new JsonObject { ["type"] = "number" },
        ContractTypeRefKind.DateTime => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        ContractTypeRefKind.AnyJson => new JsonObject(),
        ContractTypeRefKind.Named => new JsonObject { ["$ref"] = RefFor(reference.TypeId!, aggregate) },
        ContractTypeRefKind.Array => new JsonObject { ["type"] = "array", ["items"] = SchemaFor(reference.ElementType!, aggregate) },
        ContractTypeRefKind.Map => new JsonObject { ["type"] = "object", ["additionalProperties"] = SchemaFor(reference.ElementType!, aggregate) },
        _ => throw new ArgumentOutOfRangeException(nameof(reference))
    };

    private static JsonObject IntegerSchema(ContractTypeRef reference)
    {
        var schema = new JsonObject { ["type"] = "integer" };
        if (reference.Minimum.HasValue)
            schema["minimum"] = reference.Minimum.Value;
        if (reference.Maximum.HasValue)
            schema["maximum"] = reference.Maximum.Value;
        return schema;
    }

    private static JsonObject NullableSchema(JsonObject schema)
    {
        if (schema.Count == 0)
            return schema;
        return new JsonObject
        {
            ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" })
        };
    }

    private static JsonObject BuildOpenRpc(ContractIr ir)
    {
        var typesById = ir.Types.ToDictionary(static type => type.Id, StringComparer.Ordinal);
        var methods = ir.Methods.Where(static method => method.Kind == "request")
            .Select(method => (JsonNode)new JsonObject
            {
                ["name"] = method.Name,
                ["paramStructure"] = "by-name",
                ["params"] = new JsonArray(new JsonObject
                {
                    ["name"] = "params",
                    ["required"] = true,
                    ["schema"] = new JsonObject { ["$ref"] = "./" + typesById[method.ParamsType].SchemaPath }
                }),
                ["result"] = new JsonObject
                {
                    ["name"] = "result",
                    ["schema"] = new JsonObject { ["$ref"] = "./" + typesById[method.ResultType].SchemaPath }
                },
                ["x-dotcraft-direction"] = method.Direction,
                ["x-dotcraft-kind"] = method.Kind,
                ["x-dotcraft-module"] = method.Module,
                ["x-dotcraft-capability"] = method.Capability,
                ["x-dotcraft-scope"] = method.Scope,
                ["x-dotcraft-notification-opt-out"] = method.NotificationOptOut,
                ["x-dotcraft-stability"] = method.Stability,
                ["x-dotcraft-since"] = method.Since,
                ["x-dotcraft-spec-ref"] = method.SpecRef,
                ["x-dotcraft-errors"] = new JsonArray(method.Errors.Select(static error => JsonValue.Create(error)).ToArray())
            }).ToArray();

        return new JsonObject
        {
            ["openrpc"] = "1.3.2",
            ["info"] = new JsonObject
            {
                ["title"] = "DotCraft AppServer",
                ["version"] = ir.ContractVersion
            },
            ["methods"] = new JsonArray(methods)
        };
    }

    private static void ValidateJsonArtifacts(IReadOnlyDictionary<string, string> files)
    {
        foreach (var pair in files.Where(static pair => pair.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            try
            {
                JsonNode.Parse(pair.Value);
            }
            catch (JsonException exception)
            {
                throw new ProtocolGenerationException("DPG010", $"Emitter produced invalid JSON for '{pair.Key}': {exception.Message}");
            }
        }
    }

    private static void WriteFiles(string root, IReadOnlyDictionary<string, string> files)
    {
        foreach (var pair in files)
        {
            var path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, pair.Value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void InstallFiles(string stagingRoot, string repositoryRoot, IEnumerable<string> paths)
    {
        var expected = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var relative in expected.Order(StringComparer.Ordinal))
        {
            var source = Path.Combine(stagingRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".protocolgen.tmp";
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }

        foreach (var generatedRoot in GeneratedRoots())
        {
            var root = Path.Combine(repositoryRoot, generatedRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (IsIgnoredGeneratedFile(path))
                    continue;
                var relative = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
                if (!expected.Contains(relative))
                    File.Delete(path);
            }
        }
    }

    private static IReadOnlyList<string> GeneratedRoots() =>
    [
        PackageRelativePath,
        SdkBindingArtifactGenerator.TypeScriptRoot,
        SdkBindingArtifactGenerator.PythonRoot
    ];

    private static bool IsIgnoredGeneratedFile(string path) =>
        path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase) ||
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("__pycache__", StringComparer.OrdinalIgnoreCase);

    private static string RefFor(string typeId, bool aggregate)
    {
        var name = typeId[(typeId.IndexOf('.') + 1)..];
        return aggregate ? $"#/$defs/{name}" : $"./{name}.schema.json";
    }

    private static string KindName(ContractTypeKind kind) => kind.ToString().ToLowerInvariant();

    private static string CanonicalJson(JsonNode node) =>
        Normalize(node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        })) + "\n";

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
