using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.Contracts.Tests;

public sealed class ContractKernelTests
{
    [Fact]
    public void Contracts_Reference_Only_The_Bcl()
    {
        var references = typeof(AppServerRpc).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, static name => name.StartsWith("DotCraft.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, static name => name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_Is_Unique_And_Deterministically_Ordered()
    {
        var descriptors = AppServerRpcCatalog.All;
        Assert.NotEmpty(descriptors);
        Assert.Equal(
            descriptors.Count,
            descriptors.Select(static descriptor => (descriptor.Name, descriptor.Direction, descriptor.Kind)).Distinct().Count());

        var identities = descriptors
            .Select(static descriptor => $"{descriptor.Name}\u001f{descriptor.Direction}\u001f{descriptor.Kind}")
            .ToArray();
        Assert.Equal(identities.Order(StringComparer.Ordinal), identities);

        Assert.Contains(descriptors, static descriptor => descriptor is RpcRequest<InitializeParams, InitializeResult>);
        Assert.Contains(descriptors, static descriptor => descriptor is RpcNotification<RpcEmpty> && descriptor.Direction == RpcDirection.ClientToServer);
        Assert.Contains(descriptors, static descriptor => descriptor.Kind == "request" && descriptor.Direction == RpcDirection.ServerToClient);
        Assert.Contains(descriptors, static descriptor => descriptor.Kind == "notification" && descriptor.Direction == RpcDirection.ServerToClient);
    }

    [Fact]
    public void Method_Names_Are_Generated_From_The_Catalog()
    {
        var generatedNames = typeof(AppServerMethodNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(static field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var catalogNames = AppServerRpcCatalog.All
            .Select(static descriptor => descriptor.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(catalogNames, generatedNames);
    }

    [Fact]
    public void Catalog_Covers_The_Complete_Core_Surface()
    {
        var core = AppServerRpcCatalog.All.Where(static descriptor => descriptor.Module == "core").ToArray();

        Assert.Equal(186, core.Length);
        Assert.Equal(140, core.Count(static descriptor => descriptor is { Kind: "request", Direction: RpcDirection.ClientToServer }));
        Assert.Equal(40, core.Count(static descriptor => descriptor is { Kind: "notification", Direction: RpcDirection.ServerToClient }));
        Assert.Equal(5, core.Count(static descriptor => descriptor is { Kind: "request", Direction: RpcDirection.ServerToClient }));
        Assert.Single(core, static descriptor => descriptor is { Kind: "notification", Direction: RpcDirection.ClientToServer });

        Assert.Equal("backgroundTerminals", AppServerRpc.TerminalList.Capability);
        Assert.Equal("mcpElicitation", AppServerRpc.McpServerElicitationRequest.Capability);
        Assert.Equal("threadManagement", AppServerRpc.ThreadGoalGet.Capability);
    }

    [Fact]
    public void Catalog_Covers_All_Bundled_Protocol_Modules()
    {
        var modules = AppServerRpcCatalog.All
            .GroupBy(static descriptor => descriptor.Module, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(249, AppServerRpcCatalog.All.Count);
        Assert.Equal(7, modules["acp"]);
        Assert.Equal(29, modules["app-binding"]);
        Assert.Equal(11, modules["automations"]);
        Assert.Equal(186, modules["core"]);
        Assert.Equal(8, modules["external-channel"]);
        Assert.Equal(2, modules["node-repl"]);
        Assert.Equal(6, modules["teams"]);
    }

    [Fact]
    public void RpcEmpty_Serializes_As_An_Empty_Object()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new RpcEmpty(), AppServerContractJson.Options));
    }

    [Fact]
    public void Contract_Dtos_Preserve_Unknown_Properties()
    {
        const string json = """
            {"threadId":"thread_001","futureMetadata":{"mode":"preview"}}
            """;

        var notification = JsonSerializer.Deserialize<ThreadDeletedNotification>(json, AppServerContractJson.Options)!;
        var roundTrip = JsonSerializer.SerializeToElement(notification, AppServerContractJson.Options);

        Assert.Equal("thread_001", notification.ThreadId);
        Assert.Equal("preview", roundTrip.GetProperty("futureMetadata").GetProperty("mode").GetString());
    }

    [Fact]
    public void Optional_Preserves_Missing_Null_And_Value()
    {
        var options = new JsonSerializerOptions(AppServerContractJson.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AppServerContractJsonContext.Default,
                new DefaultJsonTypeInfoResolver())
        };

        Assert.Equal("{}", JsonSerializer.Serialize(new OptionalProbe(), options));
        Assert.Equal("{\"value\":null}", JsonSerializer.Serialize(new OptionalProbe { Value = Optional<string>.FromValue(null) }, options));
        Assert.Equal("{\"value\":\"set\"}", JsonSerializer.Serialize(new OptionalProbe { Value = "set" }, options));

        var missing = JsonSerializer.Deserialize<OptionalProbe>("{}", options)!;
        var explicitNull = JsonSerializer.Deserialize<OptionalProbe>("{\"value\":null}", options)!;
        Assert.False(missing.Value.IsSet);
        Assert.True(explicitNull.Value.IsSet);
        Assert.Null(explicitNull.Value.Value);
    }

    [Fact]
    public void Safe_Integer_Contracts_Preserve_Number_And_Optional_Null_Wire_Shapes()
    {
        var goal = new ThreadGoalWire
        {
            ThreadId = "thread_001",
            Objective = "finish",
            Status = "active",
            CreatedAt = JsonSafeIntegerAttribute.Minimum,
            TimeUsedSeconds = 12,
            TokenBudget = Optional<long?>.FromValue(null),
            TokensUsed = JsonSafeIntegerAttribute.Maximum,
            UpdatedAt = 42
        };

        var json = JsonSerializer.SerializeToElement(goal, AppServerContractJson.Options);

        Assert.Equal(JsonValueKind.Number, json.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonSafeIntegerAttribute.Minimum, json.GetProperty("createdAt").GetInt64());
        Assert.Equal(JsonSafeIntegerAttribute.Maximum, json.GetProperty("tokensUsed").GetInt64());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("tokenBudget").ValueKind);

        var missingBudget = JsonSerializer.SerializeToElement(new ThreadGoalSetParams(), AppServerContractJson.Options);
        var valueBudget = JsonSerializer.SerializeToElement(
            new ThreadGoalSetParams { TokenBudget = 1_000L },
            AppServerContractJson.Options);

        Assert.False(missingBudget.TryGetProperty("tokenBudget", out _));
        Assert.Equal(1_000L, valueBudget.GetProperty("tokenBudget").GetInt64());
    }

    [Fact]
    public void Initial_Contract_Slice_RoundTrips_Shared_Fixtures()
    {
        using var fixture = LoadFixture();
        var catalog = AppServerRpcCatalog.All;

        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pending = new Dictionary<string, IRpcMethodDescriptor>(StringComparer.Ordinal);
            foreach (var message in testCase.GetProperty("messages").EnumerateArray())
            {
                if (message.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString()!;
                    var descriptor = catalog.SingleOrDefault(candidate => candidate.Name == method);
                    if (descriptor is null)
                        continue;

                    var payload = message.GetProperty("params");
                    AssertWireEquivalent(payload, RoundTrip(payload, descriptor.ParamsType), testCase.GetProperty("name").GetString()!);
                    if (message.TryGetProperty("id", out var id))
                        pending[id.GetRawText()] = descriptor;
                }
                else if (message.TryGetProperty("id", out var responseId) &&
                         message.TryGetProperty("result", out var result) &&
                         pending.TryGetValue(responseId.GetRawText(), out var descriptor))
                {
                    AssertWireEquivalent(result, RoundTrip(result, descriptor.ResultType), testCase.GetProperty("name").GetString()!);
                }
            }
        }
    }

    private static JsonDocument LoadFixture()
    {
        const string resource = "DotCraft.Protocol.Contracts.Tests.AppServerMessagesV1.json";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    private static JsonElement RoundTrip(JsonElement value, Type type)
    {
        var model = JsonSerializer.Deserialize(value, type, AppServerContractJson.Options);
        Assert.NotNull(model);
        return JsonSerializer.SerializeToElement(model, type, AppServerContractJson.Options);
    }

    private static void AssertWireEquivalent(JsonElement expected, JsonElement actual, string caseName)
    {
        Assert.True(JsonEquivalent(expected, actual), $"Contract round-trip changed fixture case '{caseName}'.\nExpected: {expected}\nActual:   {actual}");
    }

    private static bool JsonEquivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquivalent(left, right),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                                   left.EnumerateArray().Zip(right.EnumerateArray()).All(static pair => JsonEquivalent(pair.First, pair.Second)),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static bool ObjectEquivalent(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        var rightProperties = right.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        return leftProperties.Count == rightProperties.Count &&
               leftProperties.All(pair => rightProperties.TryGetValue(pair.Key, out var value) && JsonEquivalent(pair.Value, value));
    }

    private sealed class OptionalProbe
    {
        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Optional<string> Value { get; init; }
    }
}
