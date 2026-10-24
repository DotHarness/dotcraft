namespace DotCraft.Sessions;

public static class WorktreeCreateSteps
{
    public const string CreateWorktree = "createWorktree";
    public const string CopyFiles = "copyFiles";
    public const string SetupEnvironment = "setupEnvironment";
}

public static class WorktreeEnvironmentSteps
{
    public const string CleanupEnvironment = "cleanupEnvironment";
}

public static class WorktreeSetupStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public static class WorktreeEnvironmentErrorCodes
{
    public const string EnvironmentInvalid = "environmentInvalid";
    public const string CreationCancelled = "creationCancelled";
    public const string CleanupFailed = "cleanupFailed";
}

public sealed record WorktreeSetupResult
{
    public string Status { get; init; } = WorktreeSetupStatuses.Skipped;

    public int? ExitCode { get; init; }

    public string OutputTail { get; init; } = string.Empty;
}

public sealed record WorktreeRunSetupOptions
{
    public string ThreadId { get; init; } = string.Empty;

    public string? OperationId { get; init; }

    public Action<string>? Output { get; init; }
}

public sealed record WorktreeSetEnvironmentOptions
{
    public string Path { get; init; } = string.Empty;

    public string? EnvironmentPath { get; init; }
}

public sealed class WorktreeEnvironmentException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public string? WorktreePath { get; init; }

    public int? ExitCode { get; init; }

    public string? OutputTail { get; init; }
}
