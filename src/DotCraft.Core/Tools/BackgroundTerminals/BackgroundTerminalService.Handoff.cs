namespace DotCraft.Tools.BackgroundTerminals;

public sealed partial class BackgroundTerminalService
{
    private async Task EnsureBackgroundedAsync(ActiveTerminal terminal)
    {
        await terminal.MetadataGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var metadata = terminal.ToMetadata();
            if (metadata.IsBackground || terminal.CompletionStarted || terminal.Process.HasExited)
                return;
            var background = metadata with { IsBackground = true };
            await PersistMetadataAsync(background, CancellationToken.None).ConfigureAwait(false);
            if (!terminal.TryBackground())
            {
                await PersistMetadataAsync(terminal.ToMetadata(), CancellationToken.None).ConfigureAwait(false);
                return;
            }
            _metadata[terminal.SessionId] = background;
            var snapshot = terminal.CreateSnapshot(includeOutput: false);
            if (!terminal.CompletionStarted && !terminal.Process.HasExited)
                Raise(BackgroundTerminalEventTypes.Backgrounded, snapshot, null);
        }
        finally
        {
            terminal.MetadataGate.Release();
        }
    }
}
