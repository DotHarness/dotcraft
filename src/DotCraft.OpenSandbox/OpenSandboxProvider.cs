using DotCraft.Configuration;
using DotCraft.Tools.Sandbox;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Models;

namespace DotCraft.OpenSandbox;

/// <summary>Creates Core sandbox instances backed by Alibaba OpenSandbox.</summary>
public sealed class OpenSandboxProvider(AppConfig.SandboxConfig config) : ISandboxProvider
{
    /// <inheritdoc />
    public async Task<ISandboxInstance> CreateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var createOptions = CreateOptions(config);
            var sandbox = await global::OpenSandbox.Sandbox.CreateAsync(createOptions, cancellationToken).ConfigureAwait(false);
            return new OpenSandboxInstance(sandbox);
        }
        catch (global::OpenSandbox.Core.SandboxException ex)
        {
            throw OpenSandboxExceptionMapper.Map(ex);
        }
    }

    internal static SandboxCreateOptions CreateOptions(AppConfig.SandboxConfig value) => new()
    {
        ConnectionConfig = new ConnectionConfig(new ConnectionConfigOptions
        {
            Domain = value.Domain,
            ApiKey = string.IsNullOrWhiteSpace(value.ApiKey) ? null : value.ApiKey,
            Protocol = value.UseHttps ? ConnectionProtocol.Https : ConnectionProtocol.Http,
            RequestTimeoutSeconds = 30,
        }),
        Image = value.Image,
        TimeoutSeconds = value.TimeoutSeconds,
        Resource = new Dictionary<string, string>
        {
            ["cpu"] = value.Cpu,
            ["memory"] = value.Memory
        },
        NetworkPolicy = CreateNetworkPolicy(value)
    };

    internal static NetworkPolicy? CreateNetworkPolicy(AppConfig.SandboxConfig value) =>
        value.NetworkPolicy.ToLowerInvariant() switch
        {
            "deny" => new NetworkPolicy
            {
                DefaultAction = NetworkRuleAction.Deny
            },
            "allow" => null,
            "custom" when value.AllowedEgressDomains.Count > 0 => new NetworkPolicy
            {
                DefaultAction = NetworkRuleAction.Deny,
                Egress = value.AllowedEgressDomains
                    .Select(domain => new NetworkRule
                    {
                        Action = NetworkRuleAction.Allow,
                        Target = domain
                    })
                    .ToList()
            },
            _ => null
        };
}

internal sealed class OpenSandboxInstance(global::OpenSandbox.Sandbox sandbox) : ISandboxInstance
{
    public string Id => sandbox.Id;

    public Task<SandboxCommandResult> RunCommandAsync(
        string command,
        SandboxCommandOptions? options = null,
        SandboxCommandHandlers? handlers = null,
        CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(async () =>
        {
            if (options == null && handlers == null)
            {
                var direct = await sandbox.Commands.RunAsync(command, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return MapExecution(direct);
            }

            var execution = await sandbox.Commands.RunAsync(
                    command,
                    new RunCommandOptions { TimeoutSeconds = options?.TimeoutSeconds ?? 0 },
                    new ExecutionHandlers
                    {
                        OnInit = handlers?.OnInitialized == null
                            ? null
                            : init => handlers.OnInitialized(init.Id)
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return MapExecution(execution);
        });

    public Task InterruptCommandAsync(string executionId, CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(
            () => sandbox.Commands.InterruptAsync(executionId, cancellationToken));

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(
            () => sandbox.Files.ReadFileAsync(path, cancellationToken: cancellationToken));

    public Task CreateDirectoriesAsync(
        IReadOnlyList<SandboxDirectoryEntry> entries,
        CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(() => sandbox.Files.CreateDirectoriesAsync(
            MapDirectoryEntries(entries),
            cancellationToken));

    public Task WriteFilesAsync(
        IReadOnlyList<SandboxWriteEntry> entries,
        CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(() => sandbox.Files.WriteFilesAsync(
            MapWriteEntries(entries),
            cancellationToken));

    public Task KillAsync(CancellationToken cancellationToken = default) =>
        OpenSandboxExceptionMapper.RunAsync(() => sandbox.KillAsync(cancellationToken));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await sandbox.DisposeAsync().ConfigureAwait(false);
        }
        catch (global::OpenSandbox.Core.SandboxException ex)
        {
            throw OpenSandboxExceptionMapper.Map(ex);
        }
    }

    internal static SandboxCommandResult MapExecution(Execution execution) => new(
        execution.Logs.Stdout.Select(line => new SandboxCommandLogLine(line.Text)).ToArray(),
        execution.Logs.Stderr.Select(line => new SandboxCommandLogLine(line.Text)).ToArray(),
        execution.Error == null
            ? null
            : new SandboxCommandError(execution.Error.Name, execution.Error.Value));

    internal static List<CreateDirectoryEntry> MapDirectoryEntries(
        IReadOnlyList<SandboxDirectoryEntry> entries) =>
        entries.Select(entry => new CreateDirectoryEntry
        {
            Path = entry.Path,
            Mode = entry.Mode
        }).ToList();

    internal static List<WriteEntry> MapWriteEntries(IReadOnlyList<SandboxWriteEntry> entries) =>
        entries.Select(entry => new WriteEntry
        {
            Path = entry.Path,
            Data = entry.Data,
            Mode = entry.Mode
        }).ToList();
}

internal static class OpenSandboxExceptionMapper
{
    public static SandboxProviderException Map(global::OpenSandbox.Core.SandboxException exception) =>
        new(exception.Error.Code, exception.Error.Message ?? "OpenSandbox provider failure.", exception);

    public static async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (global::OpenSandbox.Core.SandboxException ex)
        {
            throw Map(ex);
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (global::OpenSandbox.Core.SandboxException ex)
        {
            throw Map(ex);
        }
    }
}
