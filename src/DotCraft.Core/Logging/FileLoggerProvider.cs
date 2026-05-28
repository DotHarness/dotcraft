using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DotCraft.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes log entries to daily-rotated files under
/// a configurable directory. Thread-safe; uses append mode so multiple restarts on the same
/// day accumulate into a single file. Old files beyond <c>retentionDays</c> are pruned on startup.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDirectory;
    private readonly LogLevel _minLevel;
    private readonly int _retentionDays;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly FileLogSink _sink;

    public FileLoggerProvider(string logsDirectory, LogLevel minLevel, int retentionDays)
    {
        _logsDirectory = logsDirectory;
        _minLevel = minLevel;
        _retentionDays = retentionDays;

        Directory.CreateDirectory(logsDirectory);
        PurgeOldLogs();

        _sink = new FileLogSink(logsDirectory);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _minLevel, _sink));

    public void Dispose()
    {
        _loggers.Clear();
        _sink.Dispose();
    }

    private void PurgeOldLogs()
    {
        if (_retentionDays <= 0) return;

        try
        {
            var cutoff = DateTime.Today.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_logsDirectory, "dotcraft-*.log"))
            {
                // filename: dotcraft-yyyy-MM-dd.log
                var name = Path.GetFileNameWithoutExtension(file);
                var datePart = name.Length > 9 ? name[9..] : null; // strip "dotcraft-"
                if (datePart != null
                    && DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Purge failures are non-fatal.
        }
    }
}

/// <summary>
/// Manages the active <see cref="StreamWriter"/> with daily file rotation.
/// All <see cref="FileLogger"/> instances share a single sink to avoid opening
/// multiple file handles for the same process.
/// </summary>
internal sealed class FileLogSink : IDisposable
{
    private readonly string _directory;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private string _currentDate = string.Empty;

    internal FileLogSink(string directory)
    {
        _directory = directory;
    }

    internal void Write(string line)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        lock (_lock)
        {
            if (_writer == null || today != _currentDate)
            {
                _writer?.Dispose();
                var path = Path.Combine(_directory, $"dotcraft-{today}.log");
                // Allow another process (e.g. AppServer subprocess) to append the same daily file on Windows.
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                _currentDate = today;
            }

            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>
/// Per-category logger that formats and delegates to the shared <see cref="FileLogSink"/>.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly LogLevel _minLevel;
    private readonly FileLogSink _sink;

    internal FileLogger(string categoryName, LogLevel minLevel, FileLogSink sink)
    {
        // Strip leading "DotCraft." for compact log lines.
        _category = categoryName.StartsWith("DotCraft.", StringComparison.Ordinal)
            ? categoryName["DotCraft.".Length..]
            : categoryName;
        _minLevel = minLevel;
        _sink = sink;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        var levelLabel = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var message = formatter(state, exception);
        var line = $"{timestamp} [{levelLabel}] {_category} {message}";
        if (exception != null)
            line += Environment.NewLine + exception;

        _sink.Write(line);
    }
}
