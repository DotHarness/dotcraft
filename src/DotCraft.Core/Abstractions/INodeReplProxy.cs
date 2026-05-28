namespace DotCraft.Abstractions;

/// <summary>
/// Thread-bound proxy for Desktop-hosted persistent Node REPL runtime calls.
/// Core owns the tool and wire contract; Desktop owns browser automation.
/// </summary>
public interface INodeReplProxy
{
    /// <summary>
    /// Returns whether the current thread is bound to a client that declared Node REPL and browser support.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Evaluates JavaScript in the Desktop Node REPL runtime for the current thread.
    /// </summary>
    Task<NodeReplEvaluateResult?> EvaluateAsync(
        string code,
        int? timeoutSeconds = null,
        CancellationToken ct = default,
        NodeReplEvaluationMetadata? metadata = null);

}

/// <summary>
/// Optional browser session metadata forwarded with a Node REPL evaluation.
/// </summary>
public sealed class NodeReplEvaluationMetadata
{
    /// <summary>
    /// Thread whose Desktop runtime owns the evaluation.
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// Turn that initiated the evaluation, when available.
    /// </summary>
    public string? TurnId { get; set; }

    /// <summary>
    /// Browser session isolation key. Defaults to the thread ID for normal agent calls.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Browser session metadata version. Current value is 1.
    /// </summary>
    public int ProtocolVersion { get; set; } = 1;
}

public sealed class NodeReplEvaluateResult
{
    public string? Text { get; set; }

    public string? ResultText { get; set; }

    public List<NodeReplImageResult> Images { get; set; } = [];

    public List<string> Logs { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class NodeReplImageResult
{
    public string MediaType { get; set; } = "image/png";

    public string DataBase64 { get; set; } = string.Empty;
}
