namespace DotCraft.Tools;

/// <summary>Stable trusted annotation names used by the Remote Tool Host profile.</summary>
public static class RemoteToolMetadata
{
    /// <summary>Annotation set to JSON <see langword="true"/> for RPC-eligible native definitions.</summary>
    public const string RpcEligibleAnnotation = "dotcraft/rpcEligible";

    /// <summary>Returns whether a trusted native definition is eligible for remote routing.</summary>
    public static bool IsRpcEligible(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Id.Kind is ToolSourceKind.CoreNative or ToolSourceKind.PluginNative
               && definition.Annotations.TryGetValue(RpcEligibleAnnotation, out var value)
               && value.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}
