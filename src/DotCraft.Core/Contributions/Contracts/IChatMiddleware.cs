using Microsoft.Extensions.AI;

namespace DotCraft.Contributions;

/// <summary>
/// One ordered wrapper around the model call, applied from the outside in: the lowest
/// <see cref="ContributionOptions.Order"/> becomes the outermost client.
/// </summary>
public interface IChatMiddleware : IContributionContract
{
    /// <summary>Gets the stable, kebab-case middleware name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Wraps the next client of the pipeline, or returns <paramref name="inner"/> unchanged when this middleware does not apply.</summary>
    IChatClient Wrap(IChatClient inner, ChatPipelineContext context);
}
