using DotCraft.Plugins;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private readonly SemaphoreSlim _desktopTransferGate = new(1, 1);
    private PluginDesktopArtifact? _desktopArtifact;
    private string? _desktopPluginId;
    private string? _desktopRevision;
    private long _desktopOffset;
    private long _desktopSnapshotRevision;
    private string? _desktopPluginRoot;

    private async Task CloseDesktopArtifactsAsync()
    {
        await connection.Closed.ConfigureAwait(false);
        await _desktopTransferGate.WaitAsync().ConfigureAwait(false);
        try { ResetDesktopArtifact(); }
        finally { _desktopTransferGate.Release(); }
    }

    private async Task<AppServerTypedResult<Contract.PluginDesktopReadResult>> HandleDesktopReadAsync(
        AppServerTypedRequest<Contract.PluginDesktopReadParams> request, CancellationToken ct)
    {
        var p = request.Params;
        await _desktopTransferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (connection.IsClosed) throw DesktopArtifactError("DesktopArtifactDisconnected", "The connection is closed.");
            if (p.Offset < 0)
            {
                if (_desktopPluginId == p.Id && _desktopRevision == p.Revision) ResetDesktopArtifact();
                return AppServerTypedResult<Contract.PluginDesktopReadResult>.FromResult(new() { TotalBytes = 0, DataBase64 = "" });
            }
            if (p.Offset == 0) ResetDesktopArtifact();
            var mustValidate = p.Offset == 0 || _desktopArtifact == null
                || CurrentPluginSnapshotRevision != _desktopSnapshotRevision
                || !Directory.Exists(_desktopPluginRoot)
                || p.Offset + 1024 * 1024 >= _desktopArtifact.Length;
            if (mustValidate) await managementState.RunSnapshotReadAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                var plugin = RefreshPluginRuntime().Plugins.FirstOrDefault(item =>
                    PluginIds.EqualsCanonical(item.Manifest.Id, p.Id) && item.Installed && item.Enabled);
                if (plugin?.Manifest.Desktop == null)
                    throw DesktopArtifactError("DesktopArtifactUnavailable", "The plugin has no enabled installed Desktop contribution.");
                if (plugin.Manifest.Desktop.Revision != p.Revision)
                    throw DesktopArtifactError("DesktopArtifactChanged", "The Desktop plugin revision changed. Refresh the plugin list.");
                if (p.Offset == 0)
                {
                    _desktopArtifact = PluginDesktopArtifact.Create(plugin.Manifest, p.Revision);
                    _desktopPluginId = p.Id;
                    _desktopRevision = p.Revision;
                    _desktopPluginRoot = plugin.Manifest.RootPath;
                }
                _desktopSnapshotRevision = CurrentPluginSnapshotRevision;
                return Task.FromResult(true);
            }, ct).ConfigureAwait(false);

            if (_desktopArtifact == null || _desktopPluginId != p.Id || _desktopRevision != p.Revision || _desktopOffset != p.Offset)
                throw DesktopArtifactError("DesktopArtifactOffsetInvalid", "The artifact transfer is missing or its offset is invalid.");
            var length = _desktopArtifact.Length;
            var bytes = await _desktopArtifact.ReadAsync(p.Offset, ct).ConfigureAwait(false);
            _desktopOffset += bytes.Length;
            if (_desktopOffset == length) ResetDesktopArtifact();
            return AppServerTypedResult<Contract.PluginDesktopReadResult>.FromResult(new()
            {
                TotalBytes = length,
                DataBase64 = Convert.ToBase64String(bytes)
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ResetDesktopArtifact();
            throw DesktopArtifactError("DesktopArtifactInvalid", "The Desktop plugin artifact could not be read.");
        }
        catch
        {
            ResetDesktopArtifact();
            throw;
        }
        finally { _desktopTransferGate.Release(); }
    }

    private void ResetDesktopArtifact()
    {
        _desktopArtifact?.Dispose();
        _desktopArtifact = null;
        _desktopPluginId = null;
        _desktopRevision = null;
        _desktopOffset = 0;
        _desktopSnapshotRevision = 0;
        _desktopPluginRoot = null;
    }

    private static AppServerException DesktopArtifactError(string code, string message) =>
        new(AppServerErrors.InvalidParamsCode, message, new AppServerErrorData
        {
            Code = code, MessageKey = code, FallbackText = message
        });
}
