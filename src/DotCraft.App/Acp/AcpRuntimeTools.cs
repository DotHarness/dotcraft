using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Sessions;
using DotHarness.Acp.V1;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed class AcpRuntimeToolsCapability
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("tools")]
    public List<AcpRuntimeToolDescriptor>? Tools { get; set; }
}

public sealed class AcpRuntimeToolDescriptor
{
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public JsonObject? InputSchema { get; set; }

    [JsonPropertyName("acpMethod")]
    public string AcpMethod { get; set; } = "";

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonPropertyName("deferLoading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    [JsonPropertyName("approval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpRuntimeToolApprovalDescriptor? Approval { get; set; }
}

public sealed class AcpRuntimeToolApprovalDescriptor
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("targetArgument")]
    public string TargetArgument { get; set; } = "";

    [JsonPropertyName("operation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; set; }

    [JsonPropertyName("operationArgument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationArgument { get; set; }
}

internal static class AcpRuntimeTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static Contract.AcpExtensionCapability? BuildAcpClientCapability(
        ClientCapabilities? caps,
        IReadOnlyList<AcpRuntimeToolDescriptor>? runtimeDescriptors = null)
    {
        if (caps == null)
            return null;

        var fs = caps.Fs.GetValueOrDefault();
        var fsRead = fs?.ReadTextFile.GetValueOrDefault() == true;
        var fsWrite = fs?.WriteTextFile.GetValueOrDefault() == true;
        var terminal = caps.Terminal.GetValueOrDefault();
        var extensions = GetCustomExtensionFamilies(runtimeDescriptors);

        if (!fsRead && !fsWrite && !terminal && extensions.Count == 0)
            return null;

        return new Contract.AcpExtensionCapability
        {
            FsReadTextFile = fsRead ? true : null,
            FsWriteTextFile = fsWrite ? true : null,
            TerminalCreate = terminal ? true : null,
            Extensions = extensions.Count > 0 ? [.. extensions] : null
        };
    }

    internal static IReadOnlyList<string> GetCustomExtensionFamilies(
        IReadOnlyList<AcpRuntimeToolDescriptor>? runtimeDescriptors)
    {
        if (runtimeDescriptors is not { Count: > 0 })
            return [];

        return runtimeDescriptors
            .Select(descriptor => descriptor.AcpMethod?.Split('/', 2)[0])
            .Where(family => !string.IsNullOrWhiteSpace(family)
                             && family.StartsWith('_'))
            .Select(family => family!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool TryBuildRuntimeTools(
        ClientCapabilities? caps,
        out IReadOnlyList<RuntimeDynamicToolDeclarationSpec> dynamicTools,
        out IReadOnlyList<AcpRuntimeToolDescriptor> descriptors,
        out string message)
    {
        dynamicTools = [];
        descriptors = [];
        message = string.Empty;

        if (!TryExtractCapability(caps, out var capability, out message))
            return false;

        if (capability == null)
            return true;

        if (capability.Version != 1)
        {
            message = "clientCapabilities._meta.dotcraft.runtimeTools.version must be 1.";
            return false;
        }

        var requested = capability.Tools;
        if (requested is not { Count: > 0 })
            return true;

        var topLevelFunctions = new List<RuntimeDynamicToolFunctionSpec>();
        var namespacedFunctions = new Dictionary<string, List<RuntimeDynamicToolFunctionSpec>>(StringComparer.Ordinal);
        var normalizedDescriptors = new List<AcpRuntimeToolDescriptor>();
        foreach (var descriptor in requested)
        {
            var name = descriptor.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "runtimeTools[].name is required.";
                return false;
            }

            var description = descriptor.Description?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                message = $"runtime tool '{name}' must declare a description.";
                return false;
            }

            if (!TryNormalizeAcpRuntimeMethod(caps, descriptor.AcpMethod, out var acpMethod, out message))
                return false;

            var normalized = new AcpRuntimeToolDescriptor
            {
                Namespace = NormalizeOptionalString(descriptor.Namespace),
                Name = name,
                Description = description,
                InputSchema = descriptor.InputSchema?.DeepClone() as JsonObject,
                AcpMethod = acpMethod,
                Kind = NormalizeOptionalString(descriptor.Kind),
                DeferLoading = descriptor.DeferLoading,
                Approval = descriptor.Approval == null
                    ? null
                    : new AcpRuntimeToolApprovalDescriptor
                    {
                        Kind = descriptor.Approval.Kind,
                        TargetArgument = descriptor.Approval.TargetArgument,
                        Operation = descriptor.Approval.Operation,
                        OperationArgument = descriptor.Approval.OperationArgument
                    }
            };
            normalizedDescriptors.Add(normalized);
            var function = new RuntimeDynamicToolFunctionSpec
            {
                Name = normalized.Name,
                Description = normalized.Description,
                InputSchema = normalized.InputSchema?.DeepClone() as JsonObject,
                DeferLoading = normalized.DeferLoading,
                Approval = normalized.Approval == null
                    ? null
                    : new ChannelToolApprovalSpec
                    {
                        Kind = normalized.Approval.Kind,
                        TargetArgument = normalized.Approval.TargetArgument,
                        Operation = normalized.Approval.Operation,
                        OperationArgument = normalized.Approval.OperationArgument
                    }
            };

            if (normalized.Namespace is null)
            {
                topLevelFunctions.Add(function);
            }
            else
            {
                if (!namespacedFunctions.TryGetValue(normalized.Namespace, out var functions))
                {
                    functions = [];
                    namespacedFunctions[normalized.Namespace] = functions;
                }

                functions.Add(function);
            }
        }

        var specs = new List<RuntimeDynamicToolDeclarationSpec>(topLevelFunctions);
        specs.AddRange(namespacedFunctions.Select(pair => new RuntimeDynamicToolNamespaceSpec
        {
            Name = pair.Key,
            Description = $"ACP runtime tools in the '{pair.Key}' namespace.",
            Tools = [.. pair.Value]
        }));

        if (!WireDynamicToolProxy.TryValidateSpecs(specs, out message))
            return false;

        dynamicTools = specs;
        descriptors = normalizedDescriptors;
        return true;
    }

    internal static string RuntimeToolKey(string? @namespace, string name)
        => $"{NormalizeOptionalString(@namespace) ?? string.Empty}\u001F{name.Trim()}";

    internal static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    internal static bool TryValidateResult(RuntimeDynamicToolCallResult result, out string message)
    {
        var hasUsefulText = result.ContentItems?.Any(item =>
            string.Equals(item.Type, "text", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.Text)) == true;
        if (result.Success && !hasUsefulText)
        {
            message = "A successful ACP runtime tool result requires a useful text content item.";
            return false;
        }

        if (!result.Success
            && (string.IsNullOrWhiteSpace(result.ErrorCode)
                || string.IsNullOrWhiteSpace(result.ErrorMessage)))
        {
            message = "A failed ACP runtime tool result requires errorCode and errorMessage.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    internal static RuntimeDynamicToolCallResult Failed(
        string code,
        string message,
        JsonNode? structuredContent = null)
        => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new RuntimeDynamicToolContentItem { Type = "text", Text = $"{code}: {message}" }],
            StructuredContent = structuredContent
        };

    private static bool TryExtractCapability(
        ClientCapabilities? caps,
        out AcpRuntimeToolsCapability? capability,
        out string message)
    {
        capability = null;
        message = string.Empty;

        if (caps?.Meta.GetValueOrDefault() is not { } meta)
            return true;
        if (!meta.TryGetValue("dotcraft", out var dotcraftEl) || dotcraftEl.ValueKind != JsonValueKind.Object)
            return true;
        if (!dotcraftEl.TryGetProperty("runtimeTools", out var rtEl) || rtEl.ValueKind == JsonValueKind.Null)
            return true;

        try
        {
            capability = rtEl.Deserialize<AcpRuntimeToolsCapability>(JsonOptions);
            return true;
        }
        catch (JsonException ex)
        {
            message = $"Invalid clientCapabilities._meta.dotcraft.runtimeTools: {ex.Message}";
            return false;
        }
    }

    private static bool TryNormalizeAcpRuntimeMethod(
        ClientCapabilities? caps,
        string? rawMethod,
        out string method,
        out string message)
    {
        method = string.Empty;
        message = string.Empty;

        var trimmed = rawMethod?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            message = "runtimeTools[].acpMethod is required.";
            return false;
        }

        if (trimmed.StartsWith("ext/acp/", StringComparison.Ordinal))
            trimmed = trimmed["ext/acp/".Length..];

        var parts = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            message = $"runtime tool acpMethod '{rawMethod}' must include a capability prefix and method name.";
            return false;
        }

        if (parts[0].Equals("fs", StringComparison.OrdinalIgnoreCase))
        {
            var fs = caps?.Fs.GetValueOrDefault();
            var supported = parts[1] switch
            {
                "read_text_file" => fs?.ReadTextFile.GetValueOrDefault() == true,
                "write_text_file" => fs?.WriteTextFile.GetValueOrDefault() == true,
                _ => false
            };
            if (!supported)
            {
                message = $"runtime tool acpMethod '{rawMethod}' requires an advertised fs capability.";
                return false;
            }
        }
        else if (parts[0].Equals("terminal", StringComparison.OrdinalIgnoreCase))
        {
            if (caps?.Terminal.GetValueOrDefault() != true)
            {
                message = $"runtime tool acpMethod '{rawMethod}' requires an advertised terminal capability.";
                return false;
            }
        }
        else if (!parts[0].StartsWith('_'))
        {
            message = $"runtime tool acpMethod '{rawMethod}' must use an ACP custom method beginning with '_'.";
            return false;
        }

        method = string.Join('/', parts);
        return true;
    }
}
