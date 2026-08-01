using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol.Contracts;
using DotCraft.Protocol.Contracts.AppServer;
using Json.Schema;

namespace DotCraft.ProtocolGen.Tests;

public sealed class ProtocolArtifactTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Artifact_Generation_Is_Byte_Deterministic()
    {
        var first = ProtocolArtifactGenerator.Build(RepositoryRoot);
        var second = ProtocolArtifactGenerator.Build(RepositoryRoot);

        Assert.Equal(first.Keys, second.Keys);
        foreach (var path in first.Keys)
        {
            Assert.Equal(first[path], second[path]);
            Assert.DoesNotContain(RepositoryRoot, first[path], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\\', path);
        }

        using var manifest = JsonDocument.Parse(first["appserver.manifest.json"]);
        Assert.False(manifest.RootElement.TryGetProperty("generatedAt", out _));
    }

    [Fact]
    public void Sdk_Bindings_Are_Deterministic_And_Use_Repository_Relative_Paths()
    {
        var first = ProtocolArtifactGenerator.BuildRepositoryArtifacts(RepositoryRoot);
        var second = ProtocolArtifactGenerator.BuildRepositoryArtifacts(RepositoryRoot);

        Assert.Equal(first.Keys, second.Keys);
        Assert.Contains("sdk/typescript/src/generated/appserver/models.generated.ts", first.Keys);
        Assert.Contains("sdk/typescript/src/generated/appserver/client-requests.generated.ts", first.Keys);
        Assert.Contains("sdk/python/dotcraft/_generated/appserver/models_generated.py", first.Keys);
        Assert.Contains("sdk/python/dotcraft/_generated/appserver/client_methods_generated.py", first.Keys);
        Assert.All(first, pair =>
        {
            Assert.Equal(pair.Value, second[pair.Key]);
            Assert.DoesNotContain(RepositoryRoot, pair.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\\', pair.Key);
        });
    }

    [Fact]
    public void Manifest_Resolves_Every_Type_And_Method_Exactly_Once()
    {
        var artifacts = ProtocolArtifactGenerator.Build(RepositoryRoot);
        var manifest = JsonNode.Parse(artifacts["appserver.manifest.json"])!.AsObject();
        var types = manifest["types"]!.AsArray().Select(static node => node!.AsObject()).ToArray();
        var methods = manifest["methods"]!.AsArray().Select(static node => node!.AsObject()).ToArray();
        var typeIds = types.Select(static type => type["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(types.Length, typeIds.Count);
        Assert.Equal(AppServerRpcCatalog.All.Count, methods.Length);
        Assert.Equal(
            methods.Length,
            methods.Select(static method => (
                    method["name"]!.GetValue<string>(),
                    method["direction"]!.GetValue<string>(),
                    method["kind"]!.GetValue<string>()))
                .Distinct()
                .Count());
        Assert.All(methods, method =>
        {
            Assert.Contains(method["paramsType"]!.GetValue<string>(), typeIds);
            Assert.Contains(method["resultType"]!.GetValue<string>(), typeIds);
            Assert.False(string.IsNullOrWhiteSpace(method["module"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(method["scope"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(method["since"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(method["specRef"]!.GetValue<string>()));
        });
    }

    [Fact]
    public void Module_Selection_Preserves_Shared_Type_Identities()
    {
        var complete = ContractIrBuilder.Build(RepositoryRoot);
        var teams = ContractIrBuilder.SelectModules(complete, ["teams"]);

        Assert.NotEmpty(teams.Methods);
        Assert.All(teams.Methods, static method => Assert.Equal("teams", method.Module));
        Assert.Contains(teams.Modules, static module => module.Name == "teams");
        Assert.DoesNotContain(teams.Methods, static method => method.Module == "app-binding");
        Assert.All(teams.Types, type =>
            Assert.Contains(complete.Types, candidate => candidate.Id == type.Id && candidate.SchemaPath == type.SchemaPath));
    }

    [Fact]
    public void Profile_Selection_Filters_The_Ir_Before_Emitters_Run()
    {
        var complete = ContractIrBuilder.Build(RepositoryRoot);
        var target = complete.Methods[0];
        var mixed = complete with
        {
            Methods = complete.Methods
                .Select(method => method == target ? method with { Stability = "experimental" } : method)
                .ToArray()
        };

        var stable = ContractIrBuilder.SelectProfile(mixed, ContractArtifactProfile.Stable);
        var experimental = ContractIrBuilder.SelectProfile(mixed, ContractArtifactProfile.Experimental);

        Assert.DoesNotContain(stable.Methods, method => method.Name == target.Name);
        Assert.Contains(experimental.Methods, method => method.Name == target.Name);
        Assert.All(stable.Methods, static method => Assert.Equal("stable", method.Stability));
        Assert.All(stable.Types, type =>
            Assert.Contains(complete.Types, candidate => candidate.Id == type.Id && candidate.SchemaPath == type.SchemaPath));
    }

    [Fact]
    public void OpenRpc_Agrees_With_Manifest_Request_Methods()
    {
        var artifacts = ProtocolArtifactGenerator.Build(RepositoryRoot);
        var manifest = JsonNode.Parse(artifacts["appserver.manifest.json"])!.AsObject();
        var openRpc = JsonNode.Parse(artifacts["openrpc.json"])!.AsObject();
        var expected = manifest["methods"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Where(static method => method["kind"]!.GetValue<string>() == "request")
            .ToDictionary(static method => method["name"]!.GetValue<string>(), StringComparer.Ordinal);
        var actual = openRpc["methods"]!.AsArray()
            .Select(static node => node!.AsObject())
            .ToDictionary(static method => method["name"]!.GetValue<string>(), StringComparer.Ordinal);

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value["direction"]!.GetValue<string>(), actual[pair.Key]["x-dotcraft-direction"]!.GetValue<string>());
            Assert.Equal(pair.Value["module"]!.GetValue<string>(), actual[pair.Key]["x-dotcraft-module"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Contract_Hash_Is_Reproducible_From_Manifest_And_Schemas()
    {
        var artifacts = ProtocolArtifactGenerator.Build(RepositoryRoot);
        Assert.Equal(
            ProtocolArtifactGenerator.ComputeHash(artifacts) + "\n",
            artifacts["contract.sha256"]);
    }

    [Fact]
    public void Safe_Integer_Bounds_Appear_In_Manifest_And_Schemas()
    {
        var artifacts = ProtocolArtifactGenerator.Build(RepositoryRoot);
        var manifest = JsonNode.Parse(artifacts["appserver.manifest.json"])!.AsObject();
        var tokenUsage = manifest["types"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(static type => type["id"]!.GetValue<string>() == "core.TokenUsageInfo");
        var totalTokens = tokenUsage["fields"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(static field => field["name"]!.GetValue<string>() == "totalTokens");

        Assert.Equal("integer", totalTokens["type"]!.GetValue<string>());
        Assert.Equal(JsonSafeIntegerAttribute.Minimum, totalTokens["minimum"]!.GetValue<long>());
        Assert.Equal(JsonSafeIntegerAttribute.Maximum, totalTokens["maximum"]!.GetValue<long>());

        var schema = JsonNode.Parse(artifacts["schemas/core/TokenUsageInfo.schema.json"])!.AsObject();
        var totalTokensSchema = schema["properties"]!["totalTokens"]!.AsObject();
        Assert.Equal("integer", totalTokensSchema["type"]!.GetValue<string>());
        Assert.Equal(JsonSafeIntegerAttribute.Minimum, totalTokensSchema["minimum"]!.GetValue<long>());
        Assert.Equal(JsonSafeIntegerAttribute.Maximum, totalTokensSchema["maximum"]!.GetValue<long>());

        var compiledSchema = JsonSchema.FromText(schema.ToJsonString());
        Assert.True(compiledSchema.Evaluate(JsonNode.Parse("{\"totalTokens\":9007199254740991}")).IsValid);
        Assert.False(compiledSchema.Evaluate(JsonNode.Parse("{\"totalTokens\":9007199254740992}")).IsValid);
        Assert.False(compiledSchema.Evaluate(JsonNode.Parse("{\"totalTokens\":\"42\"}")).IsValid);
    }

    [Fact]
    public void Aggregate_Schemas_Validate_The_Shared_Contract_Slice()
    {
        var artifacts = ProtocolArtifactGenerator.Build(RepositoryRoot);
        var bundle = JsonNode.Parse(artifacts["schemas/appserver.schema.json"])!.AsObject();
        using var fixture = LoadFixture();
        var pending = new Dictionary<string, IRpcMethodDescriptor>(StringComparer.Ordinal);

        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            foreach (var message in testCase.GetProperty("messages").EnumerateArray())
            {
                if (message.TryGetProperty("method", out var methodElement))
                {
                    var descriptor = AppServerRpcCatalog.All.SingleOrDefault(candidate => candidate.Name == methodElement.GetString());
                    if (descriptor is null)
                        continue;
                    AssertSchemaValid(artifacts, bundle, descriptor.ParamsType, message.GetProperty("params"), testCase.GetProperty("name").GetString()!);
                    if (message.TryGetProperty("id", out var id))
                        pending[id.GetRawText()] = descriptor;
                }
                else if (message.TryGetProperty("id", out var responseId) &&
                         message.TryGetProperty("result", out var result) &&
                         pending.TryGetValue(responseId.GetRawText(), out var descriptor))
                {
                    AssertSchemaValid(artifacts, bundle, descriptor.ResultType, result, testCase.GetProperty("name").GetString()!);
                }
            }
        }
    }

    [Fact]
    public void Validate_And_Check_Do_Not_Write_Tracked_Artifacts()
    {
        var temporaryRoot = CreateTemporaryRepository();
        try
        {
            var packageRoot = Path.Combine(
                temporaryRoot,
                ProtocolArtifactGenerator.PackageRelativePath.Replace('/', Path.DirectorySeparatorChar));
            ProtocolArtifactGenerator.Build(temporaryRoot);
            Assert.False(Directory.Exists(packageRoot));

            ProtocolArtifactGenerator.Generate(temporaryRoot);
            var firstGeneration = SnapshotPackage(packageRoot);
            ProtocolArtifactGenerator.Generate(temporaryRoot);
            var secondGeneration = SnapshotPackage(packageRoot);
            Assert.Equal(firstGeneration.Keys, secondGeneration.Keys);
            Assert.All(firstGeneration, pair => Assert.Equal(pair.Value, secondGeneration[pair.Key]));

            var obsoletePath = Path.Combine(packageRoot, "obsolete.json");
            File.WriteAllText(obsoletePath, "{}");
            Assert.Contains(
                $"extra: {ProtocolArtifactGenerator.PackageRelativePath}/obsolete.json",
                ProtocolArtifactGenerator.Check(temporaryRoot));
            ProtocolArtifactGenerator.Generate(temporaryRoot);
            Assert.False(File.Exists(obsoletePath));

            var manifestPath = Path.Combine(packageRoot, "appserver.manifest.json");
            File.AppendAllText(manifestPath, " ");
            var before = File.ReadAllText(manifestPath);
            var drift = ProtocolArtifactGenerator.Check(temporaryRoot);
            Assert.Contains("changed: src/DotCraft.Protocol.Contracts/Artifacts/AppServer/appserver.manifest.json", drift);
            Assert.Equal(before, File.ReadAllText(manifestPath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void Diff_Classifies_Breaking_Additive_And_Metadata_Changes()
    {
        var manifest = ProtocolArtifactGenerator.Build(RepositoryRoot)["appserver.manifest.json"];
        var changed = JsonNode.Parse(manifest)!.AsObject();
        var types = changed["types"]!.AsArray();
        var targetType = types.Select(static node => node!.AsObject())
            .First(static type => type["id"]!.GetValue<string>() == "core.ThreadReadParams");
        targetType["fields"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "futureOption",
            ["type"] = "string",
            ["required"] = false,
            ["nullable"] = true,
            ["const"] = null
        });
        var method = changed["methods"]!.AsArray().Select(static node => node!.AsObject())
            .First(static value => value["name"]!.GetValue<string>() == "thread/read");
        method["specRef"] = "specs/protocols/appserver-protocol-v2.md";
        method["direction"] = "serverToClient";
        var nullableField = targetType["fields"]!.AsArray().Select(static node => node!.AsObject())
            .First(static field => field["name"]!.GetValue<string>() == "cursor");
        nullableField["nullable"] = false;

        var changes = ContractPackageDiffer.Compare(manifest, changed.ToJsonString());
        Assert.Contains(changes, static change =>
            change.Classification == ContractChangeClassification.Breaking && change.Path == "methods/thread/read/direction");
        Assert.Contains(changes, static change =>
            change.Classification == ContractChangeClassification.Breaking && change.Path == "types/core.ThreadReadParams/fields/cursor");
        Assert.Contains(changes, static change =>
            change.Classification == ContractChangeClassification.Additive && change.Path == "types/core.ThreadReadParams/fields/futureOption");
        Assert.Contains(changes, static change =>
            change.Classification == ContractChangeClassification.MetadataOnly && change.Path == "methods/thread/read/specRef");
    }

    [Fact]
    public void Diff_Classifies_Numeric_Bound_Tightening_And_Relaxation()
    {
        var manifest = ProtocolArtifactGenerator.Build(RepositoryRoot)["appserver.manifest.json"];
        var tightened = JsonNode.Parse(manifest)!.AsObject();
        var tightenedField = FindManifestField(tightened, "core.TokenUsageInfo", "totalTokens");
        tightenedField["maximum"] = JsonSafeIntegerAttribute.Maximum - 1;

        var breaking = ContractPackageDiffer.Compare(manifest, tightened.ToJsonString());
        Assert.Contains(breaking, static change =>
            change.Classification == ContractChangeClassification.Breaking &&
            change.Path == "types/core.TokenUsageInfo/fields/totalTokens/maximum");

        var relaxed = JsonNode.Parse(manifest)!.AsObject();
        FindManifestField(relaxed, "core.TokenUsageInfo", "totalTokens").Remove("maximum");

        var additive = ContractPackageDiffer.Compare(manifest, relaxed.ToJsonString());
        Assert.Contains(additive, static change =>
            change.Classification == ContractChangeClassification.Additive &&
            change.Path == "types/core.TokenUsageInfo/fields/totalTokens/maximum");
    }

    private static void AssertSchemaValid(
        IReadOnlyDictionary<string, string> artifacts,
        JsonObject bundle,
        Type runtimeType,
        JsonElement instance,
        string caseName)
    {
        var typeName = runtimeType.Name;
        var module = runtimeType.GetCustomAttribute<ContractModuleAttribute>()?.Name ?? "core";
        var wrapper = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = bundle["$defs"]!.DeepClone(),
            ["$ref"] = $"#/$defs/{typeName}"
        };
        var schema = JsonSchema.FromText(wrapper.ToJsonString());
        var result = schema.Evaluate(JsonNode.Parse(instance.GetRawText()));
        Assert.True(result.IsValid, $"Schema rejected fixture case '{caseName}' as {typeName}: {result}");

        var typeSchema = JsonNode.Parse(artifacts[$"schemas/{module}/{typeName}.schema.json"])!.AsObject();
        RewriteLocalReferences(typeSchema);
        typeSchema["$defs"] = bundle["$defs"]!.DeepClone();
        var perTypeResult = JsonSchema.FromText(typeSchema.ToJsonString()).Evaluate(JsonNode.Parse(instance.GetRawText()));
        Assert.True(perTypeResult.IsValid, $"Per-type schema rejected fixture case '{caseName}' as {typeName}: {perTypeResult}");
    }

    private static JsonObject FindManifestField(JsonObject manifest, string typeId, string fieldName) =>
        manifest["types"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(type => type["id"]!.GetValue<string>() == typeId)["fields"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(field => field["name"]!.GetValue<string>() == fieldName);

    private static void RewriteLocalReferences(JsonNode node)
    {
        if (node is JsonObject value)
        {
            if (value["$ref"] is JsonValue reference && reference.GetValue<string>() is { } text && text.StartsWith("./", StringComparison.Ordinal))
            {
                var name = Path.GetFileName(text).Replace(".schema.json", string.Empty, StringComparison.Ordinal);
                value["$ref"] = $"#/$defs/{name}";
            }
            foreach (var child in value.ToArray())
            {
                if (child.Value is not null)
                    RewriteLocalReferences(child.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                    RewriteLocalReferences(child);
            }
        }
    }

    private static JsonDocument LoadFixture()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DotCraft.ProtocolGen.Tests.AppServerMessagesV1.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    private static string CreateTemporaryRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-protocolgen-tests", Guid.NewGuid().ToString("N"));
        foreach (var specRef in AppServerRpcCatalog.All.Select(static descriptor => descriptor.SpecRef).Distinct(StringComparer.Ordinal))
        {
            var source = Path.Combine(RepositoryRoot, specRef.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(root, specRef.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        return root;
    }

    private static Dictionary<string, byte[]> SnapshotPackage(string packageRoot) =>
        Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotcraft.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
