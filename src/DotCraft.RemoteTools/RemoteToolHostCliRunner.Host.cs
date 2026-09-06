using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DotCraft.RemoteTools;

internal static partial class RemoteToolHostCliRunner
{
    internal static async Task<int> SetupAsync(string? displayName, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        if (storage.LoadHostState() is not null)
            throw new InvalidOperationException("Remote Tool Host is already set up.");

        var state = new RemoteToolHostState
        {
            HostId = "rth_" + Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Environment.MachineName : displayName.Trim()
        };
        storage.SaveHostState(state);
        await output.WriteLineAsync($"Remote Tool Host created: {state.DisplayName}").ConfigureAwait(false);
        await output.WriteLineAsync(
            "Ask for an invitation link, then run 'dotcraft tool-host join <invite-url> --workspace <folder>'.")
            .ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> AddWorkspaceAsync(
        string workspaceId,
        string path,
        TextWriter output)
    {
        if (!WorkspaceIdPattern().IsMatch(workspaceId))
            throw new ArgumentException("The workspace id must contain only letters, digits, '.', '_' or '-'.");
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new ArgumentException("The workspace path must be an existing absolute directory.");
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        var canonical = CanonicalizeDirectory(path);
        var workspaces = new Dictionary<string, string>(state.Workspaces, StringComparer.Ordinal)
        {
            [workspaceId] = canonical
        };
        storage.SaveHostState(state with
        {
            Workspaces = workspaces,
            CatalogRevision = state.CatalogRevision + 1
        });
        await output.WriteLineAsync($"{workspaceId} -> {canonical}").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> ListWorkspacesAsync(bool json, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        var workspaces = state.Workspaces.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        if (json)
        {
            await WriteJsonAsync(output, workspaces.Select(pair => new
            {
                workspaceId = pair.Key,
                path = pair.Value
            })).ConfigureAwait(false);
            return 0;
        }

        foreach (var pair in workspaces)
            await output.WriteLineAsync($"{pair.Key}\t{pair.Value}").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RemoveWorkspaceAsync(string workspaceId, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        var workspaces = new Dictionary<string, string>(state.Workspaces, StringComparer.Ordinal);
        if (!workspaces.Remove(workspaceId))
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not registered.");
        storage.SaveHostState(state with
        {
            Workspaces = workspaces,
            CatalogRevision = state.CatalogRevision + 1
        });
        await output.WriteLineAsync($"Removed workspace '{workspaceId}'.").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> ListPoliciesAsync(bool json, TextWriter output)
    {
        var state = RequireState(new RemoteToolHostStorage());
        var policies = state.ToolPolicies.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        if (json)
        {
            await WriteJsonAsync(output, policies.Select(pair => new
            {
                toolName = pair.Key,
                policy = ToCliPolicy(pair.Value)
            })).ConfigureAwait(false);
            return 0;
        }

        foreach (var pair in policies)
            await output.WriteLineAsync($"{pair.Key}\t{ToCliPolicy(pair.Value)}").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> SetPolicyAsync(string toolName, string policy, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        var value = policy == "needs-approval" ? "needsApproval" : policy;
        var policies = new Dictionary<string, string>(state.ToolPolicies, StringComparer.Ordinal)
        {
            [toolName] = value
        };
        storage.SaveHostState(state with { ToolPolicies = policies });
        await output.WriteLineAsync($"{toolName} -> {policy}").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> StatusAsync(bool json, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        if (json)
        {
            await WriteJsonAsync(output, new
            {
                displayName = state.DisplayName,
                workspaceCount = state.Workspaces.Count,
                catalogRevision = state.CatalogRevision,
                pairings = state.Peers.Select(peer => new
                {
                    peerId = peer.PeerId,
                    hub = $"{peer.HubHost}:{peer.HubPort}",
                    label = peer.HubLabel,
                    workspaceId = peer.WorkspaceId,
                    pairedAt = peer.PairedAt
                })
            }).ConfigureAwait(false);
            return 0;
        }

        await output.WriteLineAsync($"DisplayName: {state.DisplayName}").ConfigureAwait(false);
        await output.WriteLineAsync($"Workspaces: {state.Workspaces.Count}").ConfigureAwait(false);
        await output.WriteLineAsync($"Catalog revision: {state.CatalogRevision}").ConfigureAwait(false);
        await output.WriteLineAsync($"Pairings: {state.Peers.Count}").ConfigureAwait(false);
        foreach (var peer in state.Peers)
            await output.WriteLineAsync($"  {peer.PeerId}\t{peer.HubHost}:{peer.HubPort}\t{peer.HubLabel}")
                .ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> ServeAsync(TextWriter output, CancellationToken cancellationToken)
    {
        await using var runtime = RemoteToolHostRuntime.Create();
        Task running;
        try
        {
            running = runtime.RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync($"Sharing with {runtime.Peers.Count} paired machine(s).")
            .ConfigureAwait(false);
        try
        {
            await running.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        return 0;
    }

    internal static Task<int> InstallAutostartAsync(TextWriter output) =>
        SetAutostartAsync(install: true, output);

    internal static Task<int> RemoveAutostartAsync(TextWriter output) =>
        SetAutostartAsync(install: false, output);

    private static async Task<int> SetAutostartAsync(bool install, TextWriter output)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows user-login autostart is the v1 implementation target.");
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "DotCraftRemoteToolHost";
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current user's autostart registry key.");
        if (install)
        {
            if (key.GetValue("DotCraftSatellite") is not null)
                throw new InvalidOperationException(
                    "The DotCraft tray client already starts a Remote Tool Host at sign-in. "
                    + "A machine runs at most one Remote Tool Host.");
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot resolve the DotCraft executable path.");
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            var command = string.Equals(
                Path.GetFileNameWithoutExtension(executable),
                "dotnet",
                StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entryAssembly)
                    ? $"\"{executable}\" \"{entryAssembly}\" tool-host serve"
                    : $"\"{executable}\" tool-host serve";
            key.SetValue(valueName, command, RegistryValueKind.String);
            await output.WriteLineAsync("Installed current-user Remote Tool Host autostart.").ConfigureAwait(false);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            await output.WriteLineAsync("Removed current-user Remote Tool Host autostart.").ConfigureAwait(false);
        }
        return 0;
    }

    private static RemoteToolHostState RequireState(RemoteToolHostStorage storage) =>
        storage.LoadHostState()
        ?? throw new InvalidOperationException("Remote Tool Host is not set up.");

    private static string CanonicalizeDirectory(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var info = new DirectoryInfo(fullPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath));
        return fullPath;
    }

    private static string ToCliPolicy(string value) => value == "needsApproval" ? "needs-approval" : value;

    private static Task WriteJsonAsync(TextWriter output, object value) =>
        output.WriteLineAsync(JsonSerializer.Serialize(value, RemoteToolHostProtocol.JsonOptions));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdPattern();
}
