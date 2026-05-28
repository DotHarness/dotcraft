using System.Text;

namespace DotCraft.Acp;

/// <summary>
/// File-based protocol logger for ACP debugging.
/// Captures all raw JSON-RPC traffic and handler events to a timestamped log file.
/// Thread-safe; auto-flushed so logs survive crashes.
/// </summary>
public sealed class AcpLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    private AcpLogger(StreamWriter writer)
    {
        _writer = writer;
    }

    /// <summary>
    /// Creates an AcpLogger if enabled, returns null otherwise.
    /// Log file is written to {botPath}/logs/acp-{timestamp}.log.
    /// </summary>
    public static AcpLogger? Create(string botPath, bool enabled)
    {
        if (!enabled) return null;

        var logsDir = Path.Combine(botPath, "logs");
        Directory.CreateDirectory(logsDir);

        var fileName = $"acp-{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
        var filePath = Path.Combine(logsDir, fileName);

        var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        var logger = new AcpLogger(writer);
        logger.LogEvent($"ACP debug log started — {filePath}");
        return logger;
    }

    /// <summary>Client -> Agent (incoming raw JSON).</summary>
    public void LogIncoming(string rawJson)
    {
        Write("<<<", rawJson);
    }

    /// <summary>Agent -> Client (outgoing raw JSON).</summary>
    public void LogOutgoing(string rawJson)
    {
        Write(">>>", rawJson);
    }

    /// <summary>Handler-level event.</summary>
    public void LogEvent(string message)
    {
        Write("EVT", message);
    }

    /// <summary>Error with optional exception.</summary>
    public void LogError(string message, Exception? ex = null)
    {
        if (ex != null)
            Write("ERR", $"{message}\n{ex}");
        else
            Write("ERR", message);
    }

    private void Write(string tag, string content)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        lock (_lock)
        {
            _writer.WriteLine($"{timestamp} {tag} {content}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}
