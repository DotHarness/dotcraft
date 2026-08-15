namespace DotCraft.Sessions;

/// <summary>
/// Represents one native unit of user input submitted to a session.
/// </summary>
public sealed record SessionInputPart
{
    /// <summary>Gets the input discriminator.</summary>
    public string Type { get; init; } = "text";

    /// <summary>Gets plain text content.</summary>
    public string? Text { get; init; }

    /// <summary>Gets a referenced command or skill name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets command arguments.</summary>
    public string? ArgsText { get; init; }

    /// <summary>Gets the original command invocation.</summary>
    public string? RawText { get; init; }

    /// <summary>Gets a canonical file or local-image path.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the path presented to the user.</summary>
    public string? DisplayPath { get; init; }

    /// <summary>Gets an inline image data URL.</summary>
    public string? Url { get; init; }

    /// <summary>Gets a local image media type hint.</summary>
    public string? MimeType { get; init; }

    /// <summary>Gets a local image file name hint.</summary>
    public string? FileName { get; init; }
}
