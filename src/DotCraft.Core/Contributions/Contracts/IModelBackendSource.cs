using DotCraft.Agents;

namespace DotCraft.Contributions;

public interface IModelBackendSource : IContributionContract
{
    IModelBackend Backend { get; }
}
