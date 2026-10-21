using DotCraft.Contributions;

namespace DotCraft.Sessions;

/// <summary>The dependency-injection form of <see cref="IThreadLifecycleContributor"/>, for components that observe thread lifecycle without going through the contribution registry. An observer that also implements <see cref="ITurnLifecycleContributor"/> observes turns too.</summary>
public interface IThreadLifecycleObserver : IThreadLifecycleContributor;
