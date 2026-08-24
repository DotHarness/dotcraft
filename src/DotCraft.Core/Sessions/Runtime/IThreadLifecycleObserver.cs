using DotCraft.Contributions;

namespace DotCraft.Sessions;

/// <summary>The dependency-injection form of <see cref="IThreadLifecycleContributor"/>, for components that observe thread lifecycle without going through the contribution registry.</summary>
public interface IThreadLifecycleObserver : IThreadLifecycleContributor;
