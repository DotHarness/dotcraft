namespace DotCraft.Diagnostics;

/// <summary>
/// Compatibility surface for host-rendered diagnostic messages. Applications may
/// provide <see cref="Sink"/>; Core does not write to a terminal directly.
/// </summary>
public static class MessageFormatter
{
    public static Action<string>? Sink { get; set; }

    public static void Error(string message) => Sink?.Invoke($"Error: {message}");

    public static void Warning(string message) => Sink?.Invoke($"Warning: {message}");

    public static void Success(string message) => Sink?.Invoke($"Success: {message}");

    public static void Info(string message) => Sink?.Invoke($"Info: {message}");

    public static void ToolCall(string icon, string displayText) =>
        Sink?.Invoke($"{icon} {displayText}");

    public static void ToolResult(string result)
    {
        var display = result.Length > 200 ? result[..200] + "..." : result;
        Sink?.Invoke("  " + display.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim());
    }

    public static void SubAgent(string taskId, string label) =>
        Sink?.Invoke($"🐧 SubAgent[{taskId}]: {label}");

    public static void SubAgentCompleted(string taskId) =>
        Sink?.Invoke($"✓ SubAgent [{taskId}] completed");

    public static void SubAgentFailed(string taskId, string error) =>
        Sink?.Invoke($"✗ SubAgent [{taskId}] failed: {error}");
}
