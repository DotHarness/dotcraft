using DotCraft.Abstractions;

namespace DotCraft.Context;

/// <summary>
/// Global registry of IChatContextProvider instances contributed by channel modules.
/// Providers are registered once at startup (in each module's ConfigureServices) and
/// queried on every agent turn by PromptBuilder and RuntimeContextBuilder.
/// </summary>
public static class ChatContextRegistry
{
    private static readonly List<IChatContextProvider> Providers = [];

    /// <summary>
    /// Registers a context provider. Called by each channel module at startup.
    /// </summary>
    public static void Register(IChatContextProvider provider) => Providers.Add(provider);

    /// <summary>
    /// All registered providers in registration order.
    /// </summary>
    public static IReadOnlyList<IChatContextProvider> All => Providers;
}
