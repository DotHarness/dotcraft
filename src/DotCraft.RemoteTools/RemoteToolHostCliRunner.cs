using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using DotCraft.Configuration;
using DotCraft.Security;
using Microsoft.Win32;

namespace DotCraft.RemoteTools;

public static partial class RemoteToolHostCliRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var storage = new RemoteToolHostStorage();
        if (args.Length == 0)
        {
            await WriteUsageAsync(output).ConfigureAwait(false);
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "setup" => await SetupAsync(storage, args[1..], output).ConfigureAwait(false),
                "workspace" => await WorkspaceAsync(storage, args[1..], output).ConfigureAwait(false),
                "policy" => await PolicyAsync(storage, args[1..], output).ConfigureAwait(false),
                "autostart" => await AutostartAsync(args[1..], output).ConfigureAwait(false),
                "pair" => await PairAsync(storage, args[1..], output).ConfigureAwait(false),
                "token" => await TokenAsync(storage, args[1..], output).ConfigureAwait(false),
                "status" => await StatusAsync(storage, output).ConfigureAwait(false),
                "serve" => await ServeAsync(storage, cancellationToken).ConfigureAwait(false),
                "register" => await RegisterAsync(storage, args[1..], output).ConfigureAwait(false),
                "unregister" => await UnregisterAsync(storage, args[1..], output).ConfigureAwait(false),
                "list" => await ListAsync(storage, output, cancellationToken).ConfigureAwait(false),
                "test" => await TestAsync(storage, args[1..], output, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown tool-host command '{args[0]}'.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Remote Tool Host operation cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> SetupAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        var listen = GetOption(args, "--listen")
            ?? throw new ArgumentException("setup requires --listen <https-endpoint>.");
        if (!Uri.TryCreate(listen, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.Port <= 0)
            throw new ArgumentException("--listen must be an absolute HTTPS endpoint with a port.");
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
        var pairingPath = GetOption(args, "--output")
            ?? Path.Combine(Directory.GetCurrentDirectory(), $"dotcraft-tool-host-{hostId}.pairing.json");
        WritePairingFile(pairingPath, bundle);
        await output.WriteLineAsync($"Remote Tool Host created: {hostId}").ConfigureAwait(false);
        await output.WriteLineAsync($"TLS fingerprint: {state.CertificateFingerprint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Pairing file: {Path.GetFullPath(pairingPath)}").ConfigureAwait(false);
        await output.WriteLineAsync("The pairing file contains a bearer token. Transfer it securely and delete it after registration.").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> WorkspaceAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        if (args.Length == 0)
            throw new ArgumentException("workspace requires add, list, or remove.");
        var state = RequireState(storage);
        switch (args[0].ToLowerInvariant())
        {
            case "add":
            {
                if (args.Length < 2 || !WorkspaceIdPattern().IsMatch(args[1]))
                    throw new ArgumentException("workspace add requires a workspaceId containing letters, digits, '.', '_' or '-'.");
                var path = GetOption(args[2..], "--path")
                    ?? throw new ArgumentException("workspace add requires --path <absolute-path>.");
                if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
                    throw new ArgumentException("Workspace path must be an existing absolute directory.");
                var canonical = CanonicalizeDirectory(path);
                var workspaces = new Dictionary<string, string>(state.Workspaces, StringComparer.Ordinal)
                {
                    [args[1]] = canonical
                };
                storage.SaveHostState(state with
                {
                    Workspaces = workspaces,
                    CatalogRevision = state.CatalogRevision + 1
                });
                await output.WriteLineAsync($"{args[1]} -> {canonical}").ConfigureAwait(false);
                return 0;
            }
            case "list":
                foreach (var pair in state.Workspaces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    await output.WriteLineAsync($"{pair.Key}\t{pair.Value}").ConfigureAwait(false);
                return 0;
            case "remove":
            {
                if (args.Length != 2)
                    throw new ArgumentException("workspace remove requires exactly one workspaceId.");
                var workspaces = new Dictionary<string, string>(state.Workspaces, StringComparer.Ordinal);
                if (!workspaces.Remove(args[1]))
                    throw new InvalidOperationException($"Workspace '{args[1]}' is not registered.");
                storage.SaveHostState(state with
                {
                    Workspaces = workspaces,
                    CatalogRevision = state.CatalogRevision + 1
                });
                await output.WriteLineAsync($"Removed workspace '{args[1]}'.").ConfigureAwait(false);
                return 0;
            }
            default:
                throw new ArgumentException($"Unknown workspace command '{args[0]}'.");
        }
    }

    private static async Task<int> PolicyAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        var state = RequireState(storage);
        if (args.Length == 1 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in state.ToolPolicies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                await output.WriteLineAsync($"{pair.Key}\t{pair.Value}").ConfigureAwait(false);
            return 0;
        }
        if (args.Length != 3 || !args[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("policy requires 'list' or 'set <toolName> <allow|deny|needsApproval>'.");
        var value = args[2];
        if (value is not ("allow" or "deny" or "needsApproval"))
            throw new ArgumentException("Policy must be allow, deny, or needsApproval (case-sensitive).");
        var policies = new Dictionary<string, string>(state.ToolPolicies, StringComparer.Ordinal)
        {
            [args[1]] = value
        };
        storage.SaveHostState(state with { ToolPolicies = policies });
        await output.WriteLineAsync($"{args[1]} -> {value}").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> PairAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        var state = RequireState(storage);
        var path = GetOption(args, "--output")
            ?? throw new ArgumentException("pair requires --output <pairing-file>.");
        var bundle = storage.RotateToken(state);
        WritePairingFile(path, bundle);
        await output.WriteLineAsync($"Token rotated; pairing file written to {Path.GetFullPath(path)}.").ConfigureAwait(false);
        await output.WriteLineAsync("All previously paired Agent Hosts are now revoked.").ConfigureAwait(false);
        return 0;
    }

    private static Task<int> TokenAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        if (args.Length == 0 || !args[0].Equals("rotate", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("token requires 'rotate --output <pairing-file>'.");
        return PairAsync(storage, args[1..], output);
    }

    private static async Task<int> StatusAsync(RemoteToolHostStorage storage, TextWriter output)
    {
        var state = RequireState(storage);
        await output.WriteLineAsync($"HostId: {state.HostId}").ConfigureAwait(false);
        await output.WriteLineAsync($"DisplayName: {state.DisplayName}").ConfigureAwait(false);
        await output.WriteLineAsync($"Endpoint: {state.ListenEndpoint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Certificate fingerprint: {state.CertificateFingerprint}").ConfigureAwait(false);
        await output.WriteLineAsync($"Workspaces: {state.Workspaces.Count}").ConfigureAwait(false);
        await output.WriteLineAsync($"Catalog revision: {state.CatalogRevision}").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> ServeAsync(
        RemoteToolHostStorage storage,
        CancellationToken cancellationToken)
    {
        await RemoteToolHostServer.RunAsync(
            storage,
            new AppConfig(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RegisterAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        if (args.Length != 2 || !args[0].Equals("--file", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("register accepts only --file <pairing-file>; tokens are never accepted on the command line.");
        var path = GetOption(args, "--file")
            ?? throw new ArgumentException("register requires --file <pairing-file>; tokens are not accepted on the command line.");
        var bundle = JsonSerializer.Deserialize<RemoteToolPairingBundle>(
            File.ReadAllText(path),
            RemoteToolHostProtocol.JsonOptions)
            ?? throw new InvalidOperationException("Pairing file is invalid.");
        storage.Register(bundle);
        await output.WriteLineAsync($"Registered {bundle.DisplayName} ({bundle.HostId}).").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> UnregisterAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output)
    {
        if (args.Length != 1)
            throw new ArgumentException("unregister requires exactly one hostId.");
        var removed = storage.Unregister(args[0]);
        await output.WriteLineAsync(removed ? $"Unregistered {args[0]}." : $"Host {args[0]} was not registered.")
            .ConfigureAwait(false);
        return removed ? 0 : 1;
    }

    private static async Task<int> ListAsync(
        RemoteToolHostStorage storage,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        await using var client = new RemoteToolHostClient(storage, new DenyApprovalService());
        var catalog = await client.ListAsync("cli", cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> TestAsync(
        RemoteToolHostStorage storage,
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1)
            throw new ArgumentException("test requires exactly one hostId.");
        await using var client = new RemoteToolHostClient(storage, new DenyApprovalService());
        var catalog = await client.ListAsync("cli", cancellationToken).ConfigureAwait(false);
        var host = catalog.Hosts.FirstOrDefault(item => string.Equals(item.HostId, args[0], StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Host '{args[0]}' is not registered.");
        await output.WriteLineAsync(host.Online
            ? $"Connected to {host.DisplayName}; {host.Workspaces.Count} workspace(s)."
            : $"Connection failed: {host.ErrorCode}").ConfigureAwait(false);
        return host.Online ? 0 : 1;
    }

    private static async Task<int> AutostartAsync(string[] args, TextWriter output)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows user-login autostart is the v1 implementation target.");
        if (args.Length != 1 || args[0] is not ("install" or "remove"))
            throw new ArgumentException("autostart requires install or remove.");
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "DotCraftRemoteToolHost";
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current user's autostart registry key.");
        if (args[0] == "install")
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

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new ArgumentException($"Missing value for {name}.");
            return args[i + 1];
        }
        return null;
    }

    private static void WritePairingFile(string path, RemoteToolPairingBundle bundle)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(bundle, RemoteToolHostProtocol.JsonOptions));
    }

    private static Task WriteUsageAsync(TextWriter output) => output.WriteLineAsync(
        "dotcraft tool-host setup|workspace|policy|autostart|pair|token|status|serve|register|unregister|list|test");

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdPattern();

    private sealed class DenyApprovalService : IApprovalService
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => Task.FromResult(false);
        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) => Task.FromResult(false);
        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => Task.FromResult(false);
    }
}
