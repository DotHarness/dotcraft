namespace DotCraft.Sessions;

public sealed record ThreadGitInfo
{
    public string? Branch { get; init; }

    public string? OriginUrl { get; init; }
}
