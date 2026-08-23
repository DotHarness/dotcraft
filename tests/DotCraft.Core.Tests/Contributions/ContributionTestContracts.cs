using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tests.Contributions;

/// <summary>A simple ordered contract used to observe effective list composition.</summary>
internal interface ILabelContract : IContributionContract
{
    string Label { get; }
}

/// <summary>A second contract, used to prove contribution points are isolated from one another.</summary>
internal interface INoteContract : IContributionContract
{
    string Note { get; }
}

internal sealed class LabelContribution(string label) : ILabelContract
{
    public string Label => label;

    public override string ToString() => label;
}

internal sealed class NoteContribution(string note) : INoteContract
{
    public string Note => note;
}

internal sealed class DisposableNoteContribution(string note, List<string> disposalLog) : INoteContract, IDisposable
{
    public string Note => note;

    public void Dispose() => disposalLog.Add(note);
}

internal sealed class DisposableLabelContribution(string label, List<string> disposalLog, bool throwOnDispose = false)
    : ILabelContract, IDisposable
{
    public string Label => label;

    public int DisposeCount { get; private set; }

    public void Dispose()
    {
        DisposeCount++;
        disposalLog.Add(label);
        if (throwOnDispose)
            throw new InvalidOperationException($"teardown failed for {label}");
    }
}

/// <summary>Captures formatted log lines so a test can assert what an operator would see.</summary>
internal sealed class CollectingLogger<TCategory>(List<string> lines) : ILogger<TCategory>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // The registry logs from whichever thread mutated it; concurrency tests share one list.
        lock (lines)
            lines.Add(formatter(state, exception));
    }
}
