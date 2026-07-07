using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal interface IToolNamespaceMetadata
{
    string? ToolNamespace { get; }
}

internal static class ToolNamespaceMetadataResolver
{
    public static bool TryGet(AITool tool, out string toolNamespace)
    {
        switch (tool)
        {
            case IToolNamespaceMetadata namespaced
                when TryNormalize(namespaced.ToolNamespace, out toolNamespace):
                return true;

            case IDeferredToolMetadata deferred
                when TryNormalize(deferred.DeferredToolNamespace, out toolNamespace):
                return true;

            case IDynamicToolRuntimeTool dynamicTool
                when TryNormalize(dynamicTool.Spec.Namespace, out toolNamespace):
                return true;

            case IPluginFunctionTool { PluginFunctionDescriptor: { } descriptor }
                when TryNormalize(descriptor.Namespace, out toolNamespace):
                return true;

            default:
                toolNamespace = string.Empty;
                return false;
        }
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        normalized = value.Trim();
        return true;
    }
}
