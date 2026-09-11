using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>A host-owned binding to an accepted source generation, not a plugin-supplied descriptor.</summary>
public interface IRemoteToolSourceBinding
{
    string SourceId { get; }
    ValueTask<RemoteToolSourceExport> ExportAsync(CancellationToken cancellationToken = default);
}

public sealed record RemotePluginBundle(
    string PluginId,
    string SourceGeneration,
    long SourceRevision,
    string ContentFingerprint,
    string DotnetFingerprint,
    JsonElement Settings);

public sealed record RemotePluginBundleFiles(RemotePluginBundle Bundle, string RootPath);

/// <summary>Owns immutable file copies until the consumer finishes preparing the remote source.</summary>
public sealed class RemoteToolSourceExport(
    IReadOnlyList<RemotePluginBundleFiles> bundles,
    Action release) : IDisposable
{
    private Action? _release = release;
    public IReadOnlyList<RemotePluginBundleFiles> Bundles { get; } = bundles;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
