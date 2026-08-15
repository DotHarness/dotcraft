namespace DotCraft.Sessions;

/// <summary>Client capabilities captured when a turn is admitted.</summary>
public readonly record struct SessionClientCapabilities(
    bool SupportsCommandExecutionStreaming,
    bool SupportsToolExecutionLifecycle);

/// <summary>Flows client capabilities through one session request.</summary>
public static class SessionClientCapabilitiesScope
{
    private static readonly AsyncLocal<SessionClientCapabilities?> CurrentValue = new();

    public static SessionClientCapabilities? Current
    {
        get => CurrentValue.Value;
        set => CurrentValue.Value = value;
    }
}

internal static class SessionInputMetadataKeys
{
    public const string LocalImagePath = "localImage.path";
    public const string LocalImageMimeType = "localImage.mimeType";
    public const string LocalImageFileName = "localImage.fileName";
}
