using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static partial class ResponsesToolSearchMapper
{
    private const string UndeclaredFreeformParameter = "input";

    internal readonly record struct ResponsesCallReference(string Name, bool Custom);

    internal sealed class ResponsesOutputCallState(
        IReadOnlyDictionary<string, string> freeformParameters,
        IReadOnlyDictionary<string, LiteToolCallRoute>? liteToolCalls = null)
    {
        public IReadOnlyDictionary<string, string> FreeformParameters { get; } = freeformParameters;

        public IReadOnlyDictionary<string, LiteToolCallRoute>? LiteToolCalls { get; } = liteToolCalls;

        public bool IsLiteFlatAlias(string? toolNamespace, string name) =>
            toolNamespace == OpenAIResponsesLiteRequestMapper.FunctionsNamespace &&
            LiteToolCalls?.GetValueOrDefault(name).IsFlatAlias == true;

        public Dictionary<string, string> Namespaces { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CustomCalls { get; } = new(StringComparer.Ordinal);
    }

    internal readonly record struct LiteToolCallRoute(string? ProviderFlatName, bool IsFlatAlias);

    internal static IReadOnlyDictionary<string, LiteToolCallRoute> CollectLiteToolCallRoutes(
        ChatOptions? options,
        JsonArray input)
    {
        var routes = new Dictionary<string, LiteToolCallRoute>(StringComparer.Ordinal);
        foreach (var tool in options?.Tools ?? [])
        {
            if (tool.Name == IDeferredToolSearchMarker.CanonicalName)
                continue;
            var hasNamespace = ToolNamespaceMetadataResolver.TryGet(tool, out var toolNamespace);
            if (hasNamespace && toolNamespace != OpenAIResponsesLiteRequestMapper.FunctionsNamespace)
                continue;

            var hasCanonicalIdentity =
                CanonicalToolIdentityMetadataResolver.TryGet(tool, out var canonicalName, out var providerFlatName);
            var name = hasCanonicalIdentity ? canonicalName.Name : tool.Name;
            if (hasNamespace)
            {
                routes[name] = new LiteToolCallRoute(
                    hasCanonicalIdentity ? providerFlatName : toolNamespace + "__" + name,
                    IsFlatAlias: false);
            }
            else if (!routes.TryAdd(name, new LiteToolCallRoute(tool.Name, IsFlatAlias: true))
                     && routes[name].IsFlatAlias)
            {
                routes[name] = default;
            }
        }
        if (!routes.Values.Any(route => route.IsFlatAlias))
            return routes;

        foreach (var item in input.OfType<JsonObject>())
        {
            if (ReadJsonString(item, "type") != "tool_search_output" || item["tools"] is not JsonArray tools)
                continue;
            foreach (var group in tools.OfType<JsonObject>())
            {
                if (ReadJsonString(group, "type") != "namespace"
                    || ReadJsonString(group, "name") != OpenAIResponsesLiteRequestMapper.FunctionsNamespace
                    || group["tools"] is not JsonArray members)
                    continue;
                foreach (var member in members.OfType<JsonObject>())
                {
                    if (ReadJsonString(member, "name") is { } name && routes.GetValueOrDefault(name).IsFlatAlias)
                        routes.Remove(name);
                }
            }
        }

        return routes;
    }

    internal static IReadOnlyDictionary<string, string> CollectFreeformParameters(ChatOptions? options)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in options?.Tools ?? [])
        {
            if (tool is not IOpenAIResponsesFunctionToolMetadata { FreeformInput: { } freeform })
                continue;
            var toolNamespace = ToolNamespaceMetadataResolver.TryGet(tool, out var resolved) ? resolved : null;
            parameters[FreeformKey(toolNamespace, LocalToolName(tool))] = freeform.ParameterName;
        }
        return parameters;
    }

    private static string FreeformKey(string? toolNamespace, string name) =>
        toolNamespace is null ? name : toolNamespace + "/" + name;

    private static string LocalToolName(AITool tool) =>
        CanonicalToolIdentityMetadataResolver.TryGet(tool, out var canonicalName, out _)
            ? canonicalName.Name
            : tool.Name;

    private static JsonObject CreateCustomTool(string name, string description, ToolFreeformInput freeform) =>
        new()
        {
            ["type"] = "custom",
            ["name"] = name,
            ["description"] = description,
            ["format"] = new JsonObject
            {
                ["type"] = "grammar",
                ["syntax"] = freeform.Syntax,
                ["definition"] = freeform.Definition
            }
        };

    private static bool TryCreateCustomToolCallItem(
        FunctionCallContent call,
        bool supportsFreeformTools,
        out JsonObject item)
    {
        item = null!;
        if (!supportsFreeformTools || !ProviderFunctionCallMetadata.IsCustomToolCall(call))
            return false;

        var arguments = ArgumentsToJsonObject(call.Arguments);
        if (arguments.Count != 1
            || arguments.First().Value is not JsonValue text
            || !text.TryGetValue<string>(out var input))
        {
            return false;
        }

        var toolNamespace = TryGetFunctionCallNamespace(call, out var resolved) ? resolved : null;

        item = new JsonObject
        {
            ["type"] = "custom_tool_call",
            ["call_id"] = call.CallId,
            ["name"] = call.Name,
            ["input"] = input
        };
        if (toolNamespace is not null)
            item["namespace"] = toolNamespace;
        return true;
    }

    private static bool ProjectCustomCallsAsFunctionCalls(
        JsonArray input,
        IReadOnlyDictionary<string, string> freeformParameters)
    {
        var projected = false;
        foreach (var item in input.OfType<JsonObject>())
        {
            switch (ReadJsonString(item, "type"))
            {
                case "custom_tool_call":
                    var parameter = freeformParameters.GetValueOrDefault(
                            FreeformKey(ReadJsonString(item, "namespace"), ReadJsonString(item, "name") ?? string.Empty))
                        ?? UndeclaredFreeformParameter;
                    var arguments = new JsonObject { [parameter] = ReadJsonString(item, "input") ?? string.Empty };
                    item.Remove("input");
                    item["type"] = "function_call";
                    item["arguments"] = arguments.ToJsonString(JsonOptions);
                    projected = true;
                    break;
                case "custom_tool_call_output":
                    item["type"] = "function_call_output";
                    projected = true;
                    break;
            }
        }
        return projected;
    }

    private static JsonObject CreateCustomToolCallOutputItem(FunctionResultContent result) =>
        new()
        {
            ["type"] = "custom_tool_call_output",
            ["call_id"] = result.CallId,
            ["output"] = SerializeOutput(result.Result)
        };

    private static bool TryCreateSyntheticCustomToolCall(
        ResponseItem item,
        ResponsesOutputCallState? callState,
        out FunctionCallResponseItem functionCall)
    {
        functionCall = null!;
        if (!TryReadJsonObjectFromRaw(item, out var raw)
            || !string.Equals(ReadJsonString(raw, "type"), "custom_tool_call", StringComparison.Ordinal))
        {
            return false;
        }

        var callId = ReadJsonString(raw, "call_id") ?? ReadJsonString(raw, "id");
        var name = ReadJsonString(raw, "name");
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
            return false;

        var toolNamespace = TryReadString(raw, "namespace", out var resolved)
            ? resolved
            : callState?.Namespaces.GetValueOrDefault(callId);
        if (callState?.IsLiteFlatAlias(toolNamespace, name) == true)
            toolNamespace = null;
        var parameter = callState?.FreeformParameters.GetValueOrDefault(FreeformKey(toolNamespace, name))
            ?? UndeclaredFreeformParameter;
        var arguments = new JsonObject { [parameter] = ReadJsonString(raw, "input") ?? string.Empty };
        functionCall = new FunctionCallResponseItem(
            callId,
            name,
            BinaryData.FromString(arguments.ToJsonString(JsonOptions)));
        callState?.CustomCalls.Add(callId);
        return true;
    }
}
