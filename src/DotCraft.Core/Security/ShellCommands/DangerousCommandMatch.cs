namespace DotCraft.Security.ShellCommands;

public enum DangerousCommandKind
{
    ForcedRemove,
    Other
}

public sealed record DangerousCommandMatch(DangerousCommandKind Kind, string Evidence, string Reason);
