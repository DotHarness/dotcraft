namespace DotCraft.Sessions;

/// <summary>
/// Describes a versioned JSON Thread recovery snapshot staged under the workspace
/// <c>.craft/recovery-staging</c> directory.
/// </summary>
public sealed record ThreadRecoveryPackage(
    string PackagePath,
    string ThreadId,
    string TerminalTurnId,
    int FormatVersion,
    long ByteLength,
    string Sha256);

/// <summary>Stable error codes for deterministic Thread recovery failures.</summary>
public static class ThreadRecoveryErrorCodes
{
    /// <summary>The package is malformed, incomplete, or failed integrity validation.</summary>
    public const string PackageInvalid = "ThreadRecoveryPackageInvalid";

    /// <summary>The package format or contained Session schema is unsupported.</summary>
    public const string PackageIncompatible = "ThreadRecoveryPackageIncompatible";

    /// <summary>The package belongs to a different absolute workspace.</summary>
    public const string WorkspaceMismatch = "ThreadRecoveryWorkspaceMismatch";

    /// <summary>A Thread already uses the target identity.</summary>
    public const string TargetExists = "ThreadRecoveryTargetExists";
}

/// <summary>A deterministic Thread recovery validation or installation failure.</summary>
public sealed class ThreadRecoveryException : Exception
{
    /// <summary>Creates a recovery failure with a stable machine-readable code.</summary>
    public ThreadRecoveryException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stable recovery error code.</summary>
    public string Code { get; }
}
