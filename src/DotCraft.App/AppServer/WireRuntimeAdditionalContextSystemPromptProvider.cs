using DotCraft.Context;

namespace DotCraft.AppServer;

internal sealed class WireRuntimeAdditionalContextSystemPromptProvider(
    WireRuntimeAdditionalContextProvider inner)
    : IThreadSystemPromptContextProvider
{
    public ContextPageKey ContextPageKey => inner.ContextPageKey;

    public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
        inner.GetSystemPromptSection(context);
}
