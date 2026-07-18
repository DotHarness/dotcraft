namespace DotCraft.Protocol.AppServer;

public sealed class InlineVisualizationViewOpenParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string TurnId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

public sealed class InlineVisualizationViewOpenResult
{
    public string ViewHandle { get; set; } = string.Empty;
    public string Fragment { get; set; } = string.Empty;
    public string MimeType { get; set; } = "text/html";
}

public sealed class InlineVisualizationViewMessageParams
{
    public string ViewHandle { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed class InlineVisualizationViewMessageResult
{
    public string QueuedInputId { get; set; } = string.Empty;
}

public sealed class InlineVisualizationViewCloseParams
{
    public string ViewHandle { get; set; } = string.Empty;
}

public sealed class InlineVisualizationViewCloseResult
{
    public bool Closed { get; set; }
}
