using System.Text;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal sealed class TurnDiffTracker
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Tracked> _baseline = new(PathComparer);
    private readonly Dictionary<string, Tracked> _current = new(PathComparer);
    private readonly Dictionary<string, string> _displayPath = new(PathComparer);
    private Dictionary<(string Key, long? Left, long? Right), string?> _rendered = [];
    private bool _valid = true;
    private long _nextRevision;
    private string? _unifiedDiff;
    private string _lastSnapshot = "";
    private bool _dirty;
    private int _renderedDiffCount;

    internal int RenderedDiffCount
    {
        get
        {
            lock (_lock)
                return _renderedDiffCount;
        }
    }

    public void Track(FileChangeRecord change)
    {
        lock (_lock)
        {
            if (!_valid)
                return;

            try
            {
                var key = change.FullPath;
                if (_displayPath.TryAdd(key, change.DisplayPath) && change.Before is { } before)
                    _baseline[key] = NewTracked(before);
                _current[key] = NewTracked(change.After);
                Refresh();
            }
            catch (Exception)
            {
                // The write already succeeded, so a tracking failure only makes the aggregate unavailable.
                InvalidateCore();
            }
        }
    }

    public void Invalidate()
    {
        lock (_lock)
            InvalidateCore();
    }

    public bool TryTakeSnapshot(out string diff)
    {
        lock (_lock)
        {
            diff = _unifiedDiff ?? "";
            var changed = _dirty && diff != _lastSnapshot;
            _dirty = false;
            if (changed)
                _lastSnapshot = diff;
            return changed;
        }
    }

    private void Refresh()
    {
        var previous = _rendered;
        _rendered = [];
        var aggregated = new StringBuilder();
        var keys = _baseline.Keys
            .Union(_current.Keys, PathComparer)
            .OrderBy(key => _displayPath[key], StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var baseline = _baseline.GetValueOrDefault(key);
            var current = _current.GetValueOrDefault(key);
            var cacheKey = (key, baseline?.Revision, current?.Revision);
            if (!previous.TryGetValue(cacheKey, out var diff))
            {
                _renderedDiffCount++;
                var rendered = UnifiedDiffRenderer.Render(
                    _displayPath[key],
                    baseline?.Content,
                    current?.Content,
                    UnifiedDiffLimits.Aggregate);
                if (rendered.Truncated)
                {
                    InvalidateCore();
                    return;
                }
                diff = rendered.Diff;
            }
            _rendered[cacheKey] = diff;

            if (diff is null)
                continue;
            aggregated.Append(diff);
            if (!diff.EndsWith('\n'))
                aggregated.Append('\n');
            if (aggregated.Length > UnifiedDiffLimits.Aggregate.MaxDiffChars)
            {
                InvalidateCore();
                return;
            }
        }

        _unifiedDiff = aggregated.Length == 0 ? null : aggregated.ToString();
        _dirty = true;
    }

    private void InvalidateCore()
    {
        _valid = false;
        _rendered.Clear();
        _unifiedDiff = null;
        _dirty = true;
    }

    private Tracked NewTracked(string content) => new(content, _nextRevision++);

    private sealed record Tracked(string Content, long Revision);
}
