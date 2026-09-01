using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using DotCraft.Configuration;
using DotCraft.Security;
using Microsoft.Win32;

namespace DotCraft.RemoteTools;

internal static partial class RemoteToolHostCliRunner
{
    internal static async Task<int> SetupAsync(
        string listen,
        string? pairingPath,
        TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        if (!Uri.TryCreate(listen, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.Port <= 0)
            throw new ArgumentException("The endpoint must be an absolute HTTPS URL with a port.");
        if (storage.LoadHostState() is not null)
            throw new InvalidOperationException("Remote Tool Host is already set up.");

        var certificate = RemoteToolCertificate.Create(endpoint.ToString(), storage.CertificatePath);
        var token = TokenUtilities.GenerateToken();
        var hostId = "rth_" + Guid.NewGuid().ToString("N");
        var state = new RemoteToolHostState
        {
            HostId = hostId,
            DisplayName = Environment.MachineName,
            ListenEndpoint = endpoint.ToString().TrimEnd('/'),
            CertificatePath = storage.CertificatePath,
            CertificateFingerprint = RemoteToolCertificate.Fingerprint(certificate),
            TokenHash = TokenUtilities.HashToken(token)
        };
        certificate.Dispose();
        storage.SaveHostState(state);
        var bundle = new RemoteToolPairingBundle
        {
            HostId = hostId,
            DisplayName = state.DisplayName,
            Endpoint = state.ListenEndpoint,
            CertificateFingerprint = state.CertificateFingerprint,
            Token = token
        };
        pairingPath ??= DefaultPairingPath(hostId);
        WritePairingFile(pairingPath, bundle);
        await output.WriteLineAsync($"Remote Tool Host created: {hostId}").ConfigureAwait(false);
        await output.WriteLineAsync($"TLS fingerprint: {state.CertificateFingerprint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Pairing file: {Path.GetFullPath(pairingPath)}").ConfigureAwait(false);
        await output.WriteLineAsync("The pairing file contains a bearer token. Transfer it securely and delete it after registration.").ConfigureAwait(false);
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

    internal static async Task<int> RotateTokenAsync(string? pairingPath, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var state = RequireState(storage);
        var path = pairingPath ?? DefaultPairingPath(state.HostId);
        var bundle = storage.RotateToken(state);
        WritePairingFile(path, bundle);
        await output.WriteLineAsync($"Token rotated; pairing file written to {Path.GetFullPath(path)}.").ConfigureAwait(false);
        await output.WriteLineAsync("All previously paired Agent Hosts are now revoked.").ConfigureAwait(false);
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
                hostId = state.HostId,
                displayName = state.DisplayName,
                endpoint = state.ListenEndpoint,
                certificateFingerprint = state.CertificateFingerprint,
                workspaceCount = state.Workspaces.Count,
                catalogRevision = state.CatalogRevision
            }).ConfigureAwait(false);
            return 0;
        }

        await output.WriteLineAsync($"HostId: {state.HostId}").ConfigureAwait(false);
        await output.WriteLineAsync($"DisplayName: {state.DisplayName}").ConfigureAwait(false);
        await output.WriteLineAsync($"Endpoint: {state.ListenEndpoint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Certificate fingerprint: {state.CertificateFingerprint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Workspaces: {state.Workspaces.Count}").ConfigureAwait(false);
        await output.WriteLineAsync($"Catalog revision: {state.CatalogRevision}").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> ServeAsync(CancellationToken cancellationToken)
    {
        var storage = new RemoteToolHostStorage();
        await RemoteToolHostServer.RunAsync(
            storage,
            new AppConfig(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RegisterAsync(string path, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var bundle = JsonSerializer.Deserialize<RemoteToolPairingBundle>(
            File.ReadAllText(path),
            RemoteToolHostProtocol.JsonOptions)
            ?? throw new InvalidOperationException("Pairing file is invalid.");
        storage.Register(bundle);
        await output.WriteLineAsync($"Registered {bundle.DisplayName} ({bundle.HostId}).").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> UnregisterAsync(string hostId, TextWriter output)
    {
        var storage = new RemoteToolHostStorage();
        var removed = storage.Unregister(hostId);
        await output.WriteLineAsync(removed ? $"Unregistered {hostId}." : $"Host {hostId} was not registered.")
            .ConfigureAwait(false);
        return removed ? 0 : 1;
    }

    internal static async Task<int> ListAsync(
        bool json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var storage = new RemoteToolHostStorage();
        await using var client = new RemoteToolHostClient(storage, new DenyApprovalService());
        var catalog = await client.ListAsync("cli", cancellationToken).ConfigureAwait(false);
        if (json)
        {
            await WriteJsonAsync(output, catalog).ConfigureAwait(false);
            return 0;
        }

        foreach (var host in catalog.Hosts)
        {
            await output.WriteLineAsync(
                $"{host.HostId}\t{host.DisplayName}\t{(host.Online ? "online" : host.ErrorCode)}")
                .ConfigureAwait(false);
            foreach (var workspace in host.Workspaces)
                await output.WriteLineAsync($"  {workspace.WorkspaceId}\t{workspace.DisplayName}\t{(workspace.Available ? "available" : "busy")}")
                    .ConfigureAwait(false);
        }
        return 0;
    }

    internal static async Task<int> TestAsync(
        string hostId,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var storage = new RemoteToolHostStorage();
        await using var client = new RemoteToolHostClient(storage, new DenyApprovalService());
        var catalog = await client.ListAsync("cli", cancellationToken).ConfigureAwait(false);
        var host = catalog.Hosts.FirstOrDefault(item => string.Equals(item.HostId, hostId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Host '{hostId}' is not registered.");
        await output.WriteLineAsync(host.Online
            ? $"Connected to {host.DisplayName}; {host.Workspaces.Count} workspace(s)."
            : $"Connection failed: {host.ErrorCode}").ConfigureAwait(false);
        return host.Online ? 0 : 1;
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

    private static void WritePairingFile(string path, RemoteToolPairingBundle bundle)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(bundle, RemoteToolHostProtocol.JsonOptions));
    }

    private static string DefaultPairingPath(string hostId) =>
        Path.Combine(Directory.GetCurrentDirectory(), $"dotcraft-tool-host-{hostId}.pairing.json");

    private static string ToCliPolicy(string value) => value == "needsApproval" ? "needs-approval" : value;

    private static Task WriteJsonAsync(TextWriter output, object value) =>
        output.WriteLineAsync(JsonSerializer.Serialize(value, RemoteToolHostProtocol.JsonOptions));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdPattern();

    private sealed class DenyApprovalService : IApprovalService
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => Task.FromResult(false);
        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) => Task.FromResult(false);
        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => Task.FromResult(false);
    }
}
