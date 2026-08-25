using Acme.ReviewCore.Api;

namespace Acme.ReviewCore;

/// <summary>The implementation behind the exported <see cref="IReviewService"/>.</summary>
internal sealed class ReviewService : IReviewService
{
    /// <inheritdoc />
    public IReadOnlyList<string> Checklist { get; } =
    [
        "State what changed and why before reviewing how.",
        "Name the failure the change prevents, not the style it improves.",
        "Call out anything the tests would still pass without."
    ];

    /// <inheritdoc />
    public string Normalize(string text) =>
        string.Join(' ', (text ?? string.Empty).Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>An append-only activity log under the plugin's own data root; writing after disposal is a no-op.</summary>
internal sealed class ReviewJournal : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private bool _closed;

    public ReviewJournal(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        _path = Path.Combine(dataRoot, "activity.log");
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            try
            {
                File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // A sample's journal is never worth failing a turn over.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _closed = true;
    }
}
