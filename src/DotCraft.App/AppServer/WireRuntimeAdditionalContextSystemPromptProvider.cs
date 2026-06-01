using DotCraft.Abstractions;
using DotCraft.Context;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class WireRuntimeAdditionalContextSystemPromptProvider(
    WireRuntimeAdditionalContextProvider inner)
    : IThreadSystemPromptContextProvider
{
    public ContextPageKey ContextPageKey => inner.ContextPageKey;

    public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
        inner.GetSystemPromptSection(context);
}
