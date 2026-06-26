namespace DotCraft.SourceControl;

public sealed record SourceControlWriteResult
{
    public bool Continue { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public string? WarningMessage { get; init; }

    public static SourceControlWriteResult Ok(string? warning = null) => new()
    {
        Continue = true,
        WarningMessage = warning
    };

    public static SourceControlWriteResult Error(string message) => new()
    {
        Continue = false,
        ErrorMessage = message
    };
}

public interface ISourceControlWriteCoordinator
{
    Task<SourceControlWriteResult> BeforeWriteAsync(string fullPath, bool fileExists, CancellationToken ct = default);

    Task<SourceControlWriteResult> AfterWriteAsync(string fullPath, bool fileExistedBefore, CancellationToken ct = default);
}
