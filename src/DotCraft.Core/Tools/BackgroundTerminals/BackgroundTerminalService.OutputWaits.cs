using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools.BackgroundTerminals;

public sealed partial class BackgroundTerminalService
{
    private const int StallCheckIntervalMs = 5000;
    private const int StallTailChars = 1024;
    private const int MaxMatchCarryChars = 4096;
    private const int MatchedLineMaxChars = 200;

    private Timer? _stallTimer;
    private int _stallCheckRunning;

    private void StartStallWatchdog()
    {
        if (_config.StallWatchdogSeconds <= 0)
            return;
        _stallTimer = new Timer(
            static state => ((BackgroundTerminalService)state!).CheckStalls(),
            this,
            StallCheckIntervalMs,
            StallCheckIntervalMs);
    }

    internal void CheckStalls()
    {
        if (Interlocked.Exchange(ref _stallCheckRunning, 1) != 0)
            return;
        try
        {
            var threshold = TimeSpan.FromSeconds(Math.Max(1, _config.StallWatchdogSeconds));
            foreach (var terminal in _active.Values)
            {
                if (terminal.Process.HasExited || DateTimeOffset.UtcNow - terminal.QuietSince < threshold)
                    continue;
                var (tail, _, _) = terminal.SliceRaw(Math.Max(0, terminal.OutputCharacters - StallTailChars));
                if (!TerminalPromptDetector.LooksLikePrompt(tail))
                {
                    terminal.RestartQuietPeriod();
                    continue;
                }
                if (!terminal.TryReportStall())
                    continue;

                Raise(
                    BackgroundTerminalEventTypes.Stalled,
                    terminal.CreateSnapshot(maxOutputChars: _config.DefaultReadMaxOutputChars) with { WaitingForInput = true },
                    null);
                OnTerminalStalledForModel(terminal);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Background terminal stall check failed.");
        }
        finally
        {
            Volatile.Write(ref _stallCheckRunning, 0);
        }
    }

    private sealed class OutputMatchWatch : IDisposable
    {
        private readonly ActiveTerminal _terminal;
        private readonly Regex _pattern;
        private readonly object _sync = new();
        private readonly CancellationTokenSource _matched = new();
        private long _scanned;
        private string _carry = string.Empty;
        private string? _matchedLine;

        public OutputMatchWatch(ActiveTerminal terminal, Regex pattern, long from)
        {
            _terminal = terminal;
            _pattern = pattern;
            _scanned = from;
            terminal.OutputAppended += Scan;
            Scan();
        }

        public CancellationToken Token => _matched.Token;

        public string? MatchedLine
        {
            get
            {
                lock (_sync)
                    return _matchedLine;
            }
        }

        private void Scan()
        {
            lock (_sync)
            {
                if (_matchedLine != null)
                    return;
                var (text, end, lossy) = _terminal.SliceRaw(_scanned);
                if (end == _scanned)
                    return;
                _scanned = end;
                var window = lossy ? text : _carry + text;
                var line = FindMatchedLine(window);
                var lastBreak = window.LastIndexOf('\n');
                _carry = window[(lastBreak + 1)..];
                if (_carry.Length > MaxMatchCarryChars)
                    _carry = _carry[^MaxMatchCarryChars..];
                if (line == null)
                    return;
                _matchedLine = line;
            }
            _matched.Cancel();
        }

        private string? FindMatchedLine(string window)
        {
            Match match;
            try
            {
                match = _pattern.Match(window);
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
            if (!match.Success)
                return null;

            var start = match.Index == 0 ? 0 : window.LastIndexOf('\n', match.Index - 1) + 1;
            var stop = window.IndexOf('\n', match.Index);
            var line = window[start..(stop < 0 ? window.Length : stop)].Trim();
            if (line.Length == 0)
                line = match.Value.Trim();
            return line.Length <= MatchedLineMaxChars ? line : line[..MatchedLineMaxChars];
        }

        public void Dispose() => _terminal.OutputAppended -= Scan;
    }
}
