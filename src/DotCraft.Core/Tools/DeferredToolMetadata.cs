using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal sealed record DeferredToolMetadata(bool DeferLoading, string? Source, string? Namespace);

internal interface IDeferredToolMetadata
{
    bool DeferLoading { get; }

    string? DeferredToolSource { get; }

    string? DeferredToolNamespace { get; }
}

internal static class DeferredToolMetadataResolver
{
    public static bool TryGet(AITool tool, out DeferredToolMetadata metadata)
    {
        switch (tool)
        {
            case IDeferredToolMetadata deferred:
                metadata = new DeferredToolMetadata(
                    deferred.DeferLoading,
                    deferred.DeferredToolSource,
                    deferred.DeferredToolNamespace);
                return true;

            case IDynamicToolRuntimeTool dynamicTool:
                metadata = new DeferredToolMetadata(
                    dynamicTool.Spec.DeferLoading == true,
                    "dynamic",
                    dynamicTool.Spec.Namespace);
                return true;

            case IPluginFunctionTool { PluginFunctionDescriptor: { } descriptor }:
                metadata = new DeferredToolMetadata(
                    descriptor.DeferLoading == true,
                    string.IsNullOrWhiteSpace(descriptor.PluginId) ? "plugin" : descriptor.PluginId,
                    descriptor.Namespace);
                return true;

            default:
                metadata = new DeferredToolMetadata(false, null, null);
                return false;
        }
    }
}
