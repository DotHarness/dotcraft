using DotCraft.Sessions;

namespace DotCraft.Context;

/// <summary>
/// Contributes turn-local reminder text without changing the cache-stable system prompt.
/// </summary>
public interface IRuntimeContextContributor
{
    string? BuildRuntimeContext(SessionThread thread);
}
