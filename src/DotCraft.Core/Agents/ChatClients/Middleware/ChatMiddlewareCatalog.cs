using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

/// <summary>Registers DotCraft's own chat client wrappers as built-in contributions and composes the effective middleware into a pipeline.</summary>
internal static class ChatMiddlewareCatalog
{
    private static readonly Lazy<IContributionView> LazyDefaultView = new(CreateDefaultView, isThreadSafe: true);

    /// <summary>Gets the immutable process-wide view containing only the built-in middleware.</summary>
    internal static IContributionView DefaultView => LazyDefaultView.Value;

    /// <summary>Registers every built-in chat middleware into a registry.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handles; when omitted the middleware is attributed to <see cref="ContributionOrigin.Builtin"/> and lives for the registry's lifetime.</param>
    /// <returns>The handles from the outside in.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        using var batch = registry.BeginBatch();
        var handles = new List<IContributionHandle>(Definitions.Count);
        foreach (var (name, order, middleware) in Definitions)
        {
            var options = new ContributionOptions(Order: order) { TargetName = name };
            handles.Add(registrar is null
                ? registry.Add<IChatMiddleware>(middleware, options)
                : registrar.Add<IChatMiddleware>(middleware, options));
        }

        return handles;
    }

    /// <summary>Composes a pipeline by folding the effective middleware around an inner client; a middleware that throws is logged and skipped.</summary>
    /// <param name="contributions">The view to resolve from, or <see langword="null"/> to use <see cref="DefaultView"/>.</param>
    /// <returns>The outermost client. The lowest-order middleware ends up outermost.</returns>
    internal static IChatClient Compose(
        IContributionView? contributions,
        IChatClient inner,
        ChatPipelineContext context,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(context);

        // Folded from the innermost outwards so that ascending order reads outside in.
        return ContributionRead.Fold(
            (contributions ?? DefaultView).Resolve<IChatMiddleware>(context.ThreadId),
            inner,
            (client, middleware) => middleware.Wrap(client, context) ?? client,
            (middleware, ex) => logger?.LogError(
                ex,
                "Chat middleware {MiddlewareName} failed and was omitted from the {PipelineKind} pipeline.",
                SafeName(middleware),
                context.Kind),
            reverse: true);
    }

    /// <summary>The built-in middleware from the outside in, with their Tier-B target names.</summary>
    private static IReadOnlyList<(string Name, int Order, IChatMiddleware Middleware)> Definitions { get; } =
    [
        (ChatMiddlewareNames.Tracing, 200, new BuiltInChatMiddleware.Tracing()),
        (ChatMiddlewareNames.FunctionInvocation, 300, new BuiltInChatMiddleware.FunctionInvocation()),
        (ChatMiddlewareNames.SubAgentProgress, 400, new BuiltInChatMiddleware.SubAgentProgress()),
        (ChatMiddlewareNames.SubAgentTracing, 500, new BuiltInChatMiddleware.SubAgentTracing()),
        (ChatMiddlewareNames.DynamicToolInjection, 600, new BuiltInChatMiddleware.DynamicToolInjection()),
        (ChatMiddlewareNames.ImageSanitizing, 700, new BuiltInChatMiddleware.ImageSanitizing())
    ];

    private static string SafeName(IChatMiddleware middleware)
    {
        try
        {
            return middleware.Name;
        }
        catch (Exception)
        {
            return middleware.GetType().FullName ?? "<unknown>";
        }
    }

    private static IContributionView CreateDefaultView()
    {
        var registry = new ContributionRegistry();
        RegisterBuiltIns(registry);
        return registry;
    }
}
