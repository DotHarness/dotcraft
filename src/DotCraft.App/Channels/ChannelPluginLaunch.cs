using System.Collections.Concurrent;
using System.Diagnostics;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Workspaces;

namespace DotCraft.Channels;

internal sealed record ChannelPluginLaunch(
    string PluginId,
    string EntryPath,
    string DataPath,
    string NodePath,
    bool RunAsNode,
    string? QrPath,
    string SnapshotPath)
{
    private static readonly ConcurrentDictionary<(string Path, long Modified, bool RunAsNode), bool> NodeCompatibility = new();

    public static bool TryResolveNode(out string path, out bool runAsNode)
    {
        var configured = Environment.GetEnvironmentVariable("DOTCRAFT_NODE_BIN");
        runAsNode = string.Equals(Environment.GetEnvironmentVariable("DOTCRAFT_NODE_RUN_AS_NODE"), "1", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured) && File.Exists(configured))
        {
            path = configured;
            return IsSupportedNode(path, runAsNode);
        }

        runAsNode = false;
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        foreach (var item in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(item) || !Path.IsPathFullyQualified(item.Trim()))
                continue;
            var candidate = Path.Combine(item.Trim(), executable);
            if (File.Exists(candidate) && IsSupportedNode(candidate, runAsNode))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool IsSupportedNode(string path, bool runAsNode)
    {
        var key = (path, File.GetLastWriteTimeUtc(path).Ticks, runAsNode);
        return NodeCompatibility.GetOrAdd(key, static key =>
        {
            try
            {
                var info = new ProcessStartInfo(key.Path, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                if (key.RunAsNode)
                    info.Environment["ELECTRON_RUN_AS_NODE"] = "1";
                using var process = Process.Start(info);
                if (process == null)
                    return false;
                if (!process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                    return false;
                }
                var version = process.StandardOutput.ReadToEnd().Trim().TrimStart('v');
                return process.ExitCode == 0 && Version.TryParse(version, out var parsed) && parsed.Major >= 22;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return false;
            }
        });
    }

    public static ChannelPluginLaunch Stage(
        DiscoveredPlugin plugin, DotCraftPaths paths, string nodePath, bool runAsNode)
    {
        var channel = plugin.Manifest.Channel!;
        var snapshot = paths.Data.Resolve("tmp", "channel-plugins", Guid.NewGuid().ToString("N"));
        try
        {
            PluginContentTree.CopyAndFingerprint(plugin.Manifest.RootPath, snapshot, []);
            var staged = PluginManifestParser.Load(snapshot).Manifest;
            if (staged?.Channel is not { } owned || !PluginIds.EqualsCanonical(staged.Id, plugin.Manifest.Id)
                || owned.GetCompatibilityError(AppVersion.Informational) != null
                || !string.Equals(owned.ChannelName, channel.ChannelName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(owned.ConfigFileName, channel.ConfigFileName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(owned.ModuleId, channel.ModuleId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ChannelPluginInstallation.ReadId(snapshot), ChannelPluginInstallation.ReadId(plugin.Manifest.RootPath),
                    StringComparison.Ordinal))
            {
                throw new IOException("The plugin changed while staging its channel launcher.");
            }

            return new ChannelPluginLaunch(
                staged.Id, owned.EntryPath, paths.Data.RootPath, nodePath, runAsNode,
                owned.RequiresInteractiveSetup ? paths.Data.Resolve("tmp", owned.ModuleId, "qr.png") : null,
                snapshot);
        }
        catch
        {
            DeleteSnapshot(snapshot);
            throw;
        }
    }

    public void Configure(ProcessStartInfo startInfo, string workspacePath)
    {
        startInfo.FileName = NodePath;
        startInfo.ArgumentList.Add(EntryPath);
        startInfo.ArgumentList.Add("--workspace");
        startInfo.ArgumentList.Add(workspacePath);
        startInfo.Environment["DOTCRAFT_CHANNEL_CRAFT_PATH"] = DataPath;
        if (RunAsNode)
            startInfo.Environment["ELECTRON_RUN_AS_NODE"] = "1";
    }

    public void ClearQr()
    {
        if (QrPath == null)
            return;
        try
        {
            AtomicConfigDocument.RejectLinks(QrPath);
            File.Delete(QrPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Reclaim() => DeleteSnapshot(SnapshotPath);

    private static void DeleteSnapshot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
