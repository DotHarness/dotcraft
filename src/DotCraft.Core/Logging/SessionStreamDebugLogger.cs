using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Logging;

/// <summary>
/// Options for dedicated stream debug logging.
/// </summary>
public sealed class SessionStreamDebugLoggerOptions
{
    public bool Enabled { get; init; }
    public string ThreadIdFilter { get; init; } = string.Empty;
    public string TurnIdFilter { get; init; } = string.Empty;
    public bool IncludeFullText { get; init; } = true;
}

/// <summary>
/// Dedicated logger for stream-delta diagnostics. Writes JSON-line records to a standalone file.
/// </summary>
public sealed class SessionStreamDebugLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly bool _enabled;
    private readonly bool _includeFullText;
    private readonly string _threadIdFilter;
    private readonly string _turnIdFilter;
    private readonly StreamWriter? _writer;
    private readonly Lock _lock = new();

    private SessionStreamDebugLogger(
        bool enabled,
        bool includeFullText,
        string threadIdFilter,
        string turnIdFilter,
        StreamWriter? writer)
    {
        _enabled = enabled;
        _includeFullText = includeFullText;
        _threadIdFilter = threadIdFilter;
        _turnIdFilter = turnIdFilter;
        _writer = writer;
    }

    /// <summary>
    /// Whether this logger writes records.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Whether full text fields should be included in records.
    /// </summary>
    public bool IncludeFullText => _includeFullText;

    /// <summary>
    /// Creates a logger instance. Returns a disabled no-op instance when options are disabled.
    /// </summary>
    public static SessionStreamDebugLogger Create(
        string logsDirectory,
        SessionStreamDebugLoggerOptions options)
    {
        if (!options.Enabled)
            return Disabled();

        Directory.CreateDirectory(logsDirectory);
        var fileName = $"session-stream-{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
        var filePath = Path.Combine(logsDirectory, fileName);
        var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        var logger = new SessionStreamDebugLogger(
            enabled: true,
            includeFullText: options.IncludeFullText,
            threadIdFilter: options.ThreadIdFilter ?? string.Empty,
            turnIdFilter: options.TurnIdFilter ?? string.Empty,
            writer: writer);

        logger.Log(
            "stream_debug_started",
            threadId: string.Empty,
            turnId: string.Empty,
            payload: new
            {
                filePath,
                includeFullText = options.IncludeFullText,
                threadIdFilter = string.IsNullOrWhiteSpace(options.ThreadIdFilter) ? null : options.ThreadIdFilter,
                turnIdFilter = string.IsNullOrWhiteSpace(options.TurnIdFilter) ? null : options.TurnIdFilter
            });

        return logger;
    }

    /// <summary>
    /// Writes one structured stream-debug record.
    /// </summary>
    public void Log(string eventName, string threadId, string? turnId, object payload)
    {
        if (!ShouldCapture(threadId, turnId))
            return;

        var record = new
        {
            ts = DateTimeOffset.Now.ToString("O"),
            evt = eventName,
            threadId,
            turnId,
            payload
        };

        var line = JsonSerializer.Serialize(record, JsonOptions);
        lock (_lock)
        {
            _writer?.WriteLine(line);
        }
    }

    /// <summary>
    /// Returns true when the event should be captured based on enablement and filters.
    /// </summary>
    public bool ShouldCapture(string threadId, string? turnId)
    {
        if (!_enabled)
            return false;

        if (!string.IsNullOrWhiteSpace(_threadIdFilter)
            && !string.Equals(threadId, _threadIdFilter, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(_turnIdFilter)
            && !string.Equals(turnId ?? string.Empty, _turnIdFilter, StringComparison.Ordinal))
            return false;

        return true;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
        }
    }

    private static SessionStreamDebugLogger Disabled() =>
        new(
            enabled: false,
            includeFullText: false,
            threadIdFilter: string.Empty,
            turnIdFilter: string.Empty,
            writer: null);
}
