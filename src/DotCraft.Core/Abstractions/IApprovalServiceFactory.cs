using DotCraft.Security;

namespace DotCraft.Abstractions;

/// <summary>
/// Factory for creating approval services.
/// Each module can provide an approval service factory for its specific approval mechanism.
/// </summary>
public interface IApprovalServiceFactory
{
    /// <summary>
    /// Creates an approval service instance.
    /// </summary>
    /// <param name="context">The context containing configuration and dependencies.</param>
    /// <returns>An approval service instance.</returns>
    IApprovalService Create(ApprovalServiceContext context);
}
