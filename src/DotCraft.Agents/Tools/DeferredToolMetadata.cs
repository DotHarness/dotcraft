using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

public sealed record DeferredToolMetadata(bool DeferLoading, string? Source, string? Namespace);

public interface IDeferredToolMetadata
{
    bool DeferLoading { get; }

    string? DeferredToolSource { get; }

    string? DeferredToolNamespace { get; }
}

public static class DeferredToolMetadataResolver
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

            default:
                metadata = new DeferredToolMetadata(false, null, null);
                return false;
        }
    }
}
