namespace DotCraft.SessionImport;

public static class SessionImportErrorCodes
{
    public const string Busy = "import_busy";
    public const string SourceUnavailable = "import_source_unavailable";
}

/// <summary>A session import request the service refused, carrying a stable code and an English fallback message.</summary>
public sealed class SessionImportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
