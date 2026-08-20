using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Determines whether a provider response requires another sampling request before an agent turn is complete.
/// </summary>
public interface IProviderManagedContinuationPolicy
{
    /// <summary>
    /// Gets the maximum number of additional provider requests allowed after the initial request.
    /// The value must be non-negative; zero rejects the first response that requires continuation.
    /// </summary>
    int MaximumContinuations { get; }

    /// <summary>Returns whether the response must be appended to history and continued by the provider.</summary>
    bool ShouldContinue(ChatResponse response);
}
