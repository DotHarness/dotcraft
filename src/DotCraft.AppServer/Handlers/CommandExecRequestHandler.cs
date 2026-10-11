using System.Collections;
using DotCraft.Configuration;
using DotCraft.Security;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class CommandExecRequestHandler(
    IAppConfigMonitor? appConfigMonitor,
    string? hostWorkspacePath,
    AppServerConnection connection) : IAppServerDomainHandler
{
    private const int DefaultTimeoutMs = 10_000;
    private const int DefaultOutputBytesCap = 1024 * 1024;

    private readonly CancellationToken _connectionClosed = CloseToken(connection);

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.CommandExec, HandleExecAsync);
    }

    private async Task<AppServerTypedResult<Contract.CommandExecResult>> HandleExecAsync(
        AppServerTypedRequest<Contract.CommandExecParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        if (p.Command is not { Count: > 0 } || string.IsNullOrWhiteSpace(p.Command[0]))
            throw AppServerErrors.InvalidParams("'command' must name a program.");
        var timeoutMs = p.TimeoutMs.IsSet ? p.TimeoutMs.Value : DefaultTimeoutMs;
        if (timeoutMs <= 0)
            throw AppServerErrors.InvalidParams("'timeoutMs' must be positive.");
        var outputBytesCap = p.OutputBytesCap.IsSet ? p.OutputBytesCap.Value : DefaultOutputBytesCap;
        if (outputBytesCap < 0)
            throw AppServerErrors.InvalidParams("'outputBytesCap' must not be negative.");

        var cwd = ResolveWorkingDirectory(p.Cwd);
        var environment = BuildEnvironment(p.Env.IsSet ? p.Env.Value : null);
        var program = CommandExecProcess.ResolveProgram(p.Command[0], cwd, environment)
                      ?? throw CommandNotFound();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _connectionClosed);
        CommandExecProcess process;
        try
        {
            process = CommandExecProcess.Start(program, p.Command.Skip(1).ToArray(), cwd, environment);
        }
        catch (FileNotFoundException)
        {
            throw CommandNotFound();
        }

        await using (process)
        {
            var result = await process.RunAsync(TimeSpan.FromMilliseconds(timeoutMs), outputBytesCap, cts.Token);
            return AppServerTypedResult<Contract.CommandExecResult>.FromResult(new()
            {
                ExitCode = result.ExitCode,
                Stdout = result.Stdout,
                Stderr = result.Stderr
            });
        }
    }

    private string ResolveWorkingDirectory(Protocol.Optional<string> requested)
    {
        string cwd;
        if (requested.IsSet)
        {
            if (string.IsNullOrWhiteSpace(requested.Value) || !Path.IsPathFullyQualified(requested.Value))
                throw AppServerErrors.InvalidParams("'cwd' must be an absolute path.");
            cwd = Path.GetFullPath(requested.Value);
        }
        else
        {
            cwd = hostWorkspacePath ?? Directory.GetCurrentDirectory();
        }

        if (!Directory.Exists(cwd))
            throw AppServerErrors.CommandExec("DirectoryNotFound", "The working directory does not exist.");
        var blacklist = new PathBlacklist(appConfigMonitor?.Current.Security.BlacklistedPaths ?? []);
        if (blacklist.IsBlacklisted(cwd) || blacklist.IsBlacklisted(WorkspaceBoundary.ResolveSymbolicLink(cwd)))
            throw AppServerErrors.CommandExec("PathBlocked", "Access to the working directory is blocked.");
        return cwd;
    }

    private static Dictionary<string, string> BuildEnvironment(IReadOnlyDictionary<string, string?>? overrides)
    {
        var environment = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        foreach (var (name, value) in overrides ?? new Dictionary<string, string?>())
        {
            if (value is null)
                environment.Remove(name);
            else
                environment[name] = value;
        }

        return environment;
    }

    private static CancellationToken CloseToken(AppServerConnection connection)
    {
        var closed = new CancellationTokenSource();
        _ = connection.Closed.ContinueWith(_ => closed.Cancel(), TaskScheduler.Default);
        return closed.Token;
    }

    private static AppServerException CommandNotFound() =>
        AppServerErrors.CommandExec("CommandNotFound", "The program could not be found.");
}
