using System.Security.Cryptography;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Channels;

public sealed partial class ChannelRunner
{
    private readonly SemaphoreSlim _pluginChangeGate = new(1, 1);
    private readonly Dictionary<string, PluginChannelProcess> _pluginProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pluginFailures = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pluginReconciliation;
    private int _pluginConfigRevision;

    public ChannelPluginWorkspace ChannelPlugins { get; }

    private sealed record PluginChannelProcess(string Revision, ChannelHost Host, ChannelPluginLaunch Launch);

    private bool WebSocketEnabled() => _config.GetSection<AppServerConfig>("AppServer").Mode is
        AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket;

    private IReadOnlyList<ChannelEntry> ReadChannelSelections()
    {
        lock (_channelsLock)
            return ChannelEntryMap.ToDictionaryByNameLastWins(_config.Channels).Values.Select(entry => entry with { }).ToArray();
    }

    private bool HasPluginSelections()
    {
        lock (_channelsLock)
            return _config.Channels.Any(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.PluginId));
    }

    private void OnPluginConfigChanged(object? sender, AppConfigChangedEventArgs change)
    {
        if (change.Regions.Contains(ConfigChangeRegions.Plugins))
            Interlocked.Increment(ref _pluginConfigRevision);
    }

    private async Task WatchPluginChannelsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var monitor = _sp.GetService<IAppConfigMonitor>();
        if (monitor != null)
            monitor.Changed += OnPluginConfigChanged;
        try
        {
            string? last = null;
            do
            {
                if (!HasPluginSelections())
                {
                    last = null;
                    continue;
                }

                var stamp = GetPluginReconciliationStamp();
                if (stamp == last)
                    continue;

                try
                {
                    await _pluginChangeGate.WaitAsync(ct);
                    try { await ReconcilePluginChannelsAsync(forceName: null, ct); }
                    finally { _pluginChangeGate.Release(); }
                    last = stamp;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _sp.GetService<ILogger<ChannelRunner>>()?.LogWarning(ex, "Channel plugin reconciliation failed");
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (monitor != null)
                monitor.Changed -= OnPluginConfigChanged;
        }
    }

    private string GetPluginReconciliationStamp() =>
        $"{Volatile.Read(ref _pluginConfigRevision)}|{ReadRevision(_paths.Data.RootPath)}|{ReadRevision(_paths.UserData.RootPath)}";

    private static string ReadRevision(string? root)
    {
        if (root == null)
            return "";
        var path = Path.Combine(root, "imports", "revision.json");
        try
        {
            return new FileInfo(path) is { Exists: true, Length: <= 4096 } ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public async Task ApplyChannelSetEnabledAsync(ChannelEntry entry, CancellationToken ct)
    {
        await _pluginChangeGate.WaitAsync(ct);
        try
        {
            UpdateChannelSelection(entry);
            await ReconcilePluginChannelsAsync(entry.Name, ct);
        }
        finally
        {
            _pluginChangeGate.Release();
        }
    }

    private async Task ReconcilePluginChannelsAsync(string? forceName, CancellationToken ct)
    {
        var selections = ReadChannelSelections();
        var installed = selections.Any(e => e.Enabled) ? ChannelPlugins.Discover() : [];
        foreach (var entry in selections)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ReconcilePluginChannelAsync(entry, installed, string.Equals(forceName, entry.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AppServerException)
            {
                _sp.GetService<ILogger<ChannelRunner>>()?.LogWarning(ex, "Channel plugin {Channel} failed admission", entry.Name);
                await StopPluginChannelAsync(entry.Name);
                SetPluginFailure(entry.Name, "ChannelPluginAdmissionFailed");
            }
        }
    }

    private async Task ReconcilePluginChannelAsync(
        ChannelEntry entry, IReadOnlyList<DiscoveredPlugin> installed, bool forceRestart)
    {
        if (!entry.Enabled)
        {
            await StopPluginChannelAsync(entry.Name);
            SetPluginFailure(entry.Name, null);
            return;
        }

        var plugin = ChannelPluginWorkspace.Find(installed, entry.PluginId);
        if (entry.PluginActivationId == null && TryRestoreOfficialChannel(entry, plugin))
        {
            installed = ChannelPlugins.Discover();
            plugin = ChannelPluginWorkspace.Find(installed, entry.PluginId);
        }

        var failure = ChannelPlugins.GetAdmissionFailure(plugin, entry);
        var module = plugin?.Manifest.Channel;
        var activationId = plugin == null ? null : ChannelPluginInstallation.ReadId(plugin.Manifest.RootPath);
        if (failure == null)
        {
            if (plugin != null && ChannelPlugins.HasDescriptorConflict(installed, plugin))
                failure = "ChannelPluginConflictingDescriptor";
            else if (activationId == null || !string.Equals(activationId, entry.PluginActivationId, StringComparison.Ordinal))
                failure = "ChannelPluginActivationRequired";
            else if (!WebSocketEnabled())
                failure = "ChannelPluginWebSocketRequired";
        }

        string? revision = null;
        if (failure == null && module != null)
        {
            revision = GetChannelPluginRevision(plugin!.Manifest.RootPath, module.EntryPath);
            if (!forceRestart && _pluginProcesses.TryGetValue(entry.Name, out var running)
                && running.Revision == revision)
                return;

            if (ChannelPlugins.ReadSnapshot(module).MissingRequiredFields.Count > 0)
                failure = "ChannelPluginConfigMissing";
        }

        if (failure != null || module == null)
        {
            await StopPluginChannelAsync(entry.Name);
            SetPluginFailure(entry.Name, failure);
            return;
        }

        if (!ChannelPluginLaunch.TryResolveNode(out var nodePath, out var runAsNode))
        {
            await StopPluginChannelAsync(entry.Name);
            SetPluginFailure(entry.Name, "ChannelPluginNodeUnavailable");
            return;
        }

        ChannelPlugins.EnsureConfigFile(module);
        var launch = ChannelPluginLaunch.Stage(plugin!, _paths, nodePath, runAsNode);
        try
        {
            await StopPluginChannelAsync(entry.Name);
            lock (_channelsLock)
            {
                if (_channelCts?.IsCancellationRequested != false || _nativeChannels.Any(c =>
                        string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    launch.Reclaim();
                    return;
                }

                var host = CreateChannelHost_NoLock(entry);
                host.SetChannelPluginLaunch(launch);
                _pluginProcesses.Add(entry.Name, new PluginChannelProcess(revision!, host, launch));
                _pluginFailures.Remove(entry.Name);
                _allChannels.Add(host);
                _router.RegisterChannel(host);
                _channelHostRegistry.Register(entry.Name, host);
                _channelTasks?.RemoveAll(task => task.IsCompleted);
                _channelTasks?.Add(RunChannelAsync(host, _channelCts.Token));
            }
        }
        catch
        {
            launch.Reclaim();
            throw;
        }
    }

    private void SetPluginFailure(string name, string? reason)
    {
        lock (_channelsLock)
        {
            if (reason != null)
                _pluginFailures[name] = reason;
            else
                _pluginFailures.Remove(name);
        }
    }

    private async Task StopPluginChannelAsync(string name)
    {
        PluginChannelProcess? process;
        lock (_channelsLock)
        {
            if (!_pluginProcesses.Remove(name, out process))
                return;
            UnregisterChannelHost_NoLock(process.Host);
        }

        await process.Host.DisposeAsync();
        process.Launch.ClearQr();
        process.Launch.Reclaim();
    }

    private static string GetChannelPluginRevision(string root, string entryPoint)
    {
        var marker = Path.Combine(root, BuiltInPluginDeployer.MarkerFile);
        var identity = File.Exists(marker) ? File.ReadAllText(marker) : "";
        return $"{root}|{identity}|{File.GetLastWriteTimeUtc(entryPoint).Ticks}";
    }

    public Contract.ChannelSetupArtifact? GetChannelSetupArtifact(string pluginId)
    {
        lock (_channelsLock)
        {
            var process = _pluginProcesses.Values.FirstOrDefault(p => PluginIds.EqualsCanonical(p.Launch.PluginId, pluginId));
            if (process?.Launch.QrPath is not { } path || !process.Host.HasRunningProcess)
                return null;

            try
            {
                AtomicConfigDocument.RejectLinks(path);
                var file = new FileInfo(path);
                if (!file.Exists || file.Length is < 8 or > 262_144
                    || DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromMinutes(2))
                    return null;
                using var stream = File.OpenRead(path);
                if (stream.Length is < 8 or > 262_144)
                    return null;
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    return null;
                return new Contract.ChannelSetupArtifact
                {
                    Revision = Convert.ToHexString(SHA256.HashData(bytes)),
                    MimeType = "image/png",
                    Data = Convert.ToBase64String(bytes)
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
