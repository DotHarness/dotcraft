using DotCraft.Sessions;

namespace DotCraft.Tools;

/// <summary>
/// Where a thread's tools work. A worktree tool can move it during a Turn; the boundary then also
/// admits the new location's roots.
/// </summary>
public sealed class ToolLocation(ThreadWorkspaceContext initial)
{
    public ThreadWorkspaceContext Current { get; private set; } = initial;

    public string Directory => Current.Cwd;

    public IReadOnlyList<string> Roots => Current.RuntimeWorkspaceRoots;

    public void MoveTo(ThreadWorkspaceContext next) => Current = next;
}
