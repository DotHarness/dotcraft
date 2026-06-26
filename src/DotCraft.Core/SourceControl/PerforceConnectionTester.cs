using DotCraft.Utilities;
using Microsoft.Extensions.Logging;

namespace DotCraft.SourceControl;

/// <summary>Outcome of a single p4 invocation, with start/timeout failures surfaced as flags.</summary>
public readonly record struct PerforceCommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool ExecutableMissing,
    bool TimedOut)
{
    public bool Ok => !ExecutableMissing && !TimedOut && ExitCode == 0;
}

/// <summary>Abstraction over p4 invocation so the connection tester can be unit-tested with canned output.</summary>
public interface IPerforceCommandRunner
{
    Task<PerforceCommandResult> RunAsync(IReadOnlyList<string> args, string? stdinInput, CancellationToken ct);
}

/// <summary>Non-sensitive test inputs plus a transient password used only for a one-shot login.</summary>
public sealed record PerforceTestRequest
{
    public string WorkspacePath { get; init; } = string.Empty;
    public string ConnectionMode { get; init; } = SourceControlConnectionModes.P4Config;
    public string Port { get; init; } = string.Empty;
    public string Client { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Charset { get; init; } = string.Empty;
    public string P4ConfigName { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Transient login password. Never persisted, never logged, never echoed back.</summary>
    public string? Password { get; init; }
}

public sealed record PerforceReportItem(string Code, string FallbackText);

public sealed record PerforceReportIdentity(
    string? ServerAddress = null,
    string? User = null,
    string? Client = null,
    string? Charset = null,
    string? ConnectionMode = null);

public sealed record PerforceReportWorkspace(
    string? WorkspacePath = null,
    string? ClientRoot = null,
    IReadOnlyList<string>? AltRoots = null,
    bool? MappingOk = null);

public sealed record PerforceReportAuth(
    string? TicketStatus = null,
    bool LoginRequired = false,
    string? ExpiresMessage = null);

public sealed record PerforceReportDiagnostics(
    string? P4Version = null,
    int TimeoutSeconds = 30,
    int WarningCount = 0,
    string? ErrorCode = null);

/// <summary>Structured, non-sensitive result of a Perforce connection test.</summary>
public sealed record PerforceConnectionReport
{
    public string Status { get; init; } = SourceControlStatuses.Error;
    public string Code { get; init; } = PerforceErrorCodes.Unknown;
    public string Summary { get; init; } = string.Empty;
    public string FallbackText { get; init; } = string.Empty;
    public PerforceReportIdentity Identity { get; init; } = new();
    public PerforceReportWorkspace Workspace { get; init; } = new();
    public PerforceReportAuth Authentication { get; init; } = new();
    public PerforceReportDiagnostics Diagnostics { get; init; } = new();
    public IReadOnlyList<PerforceReportItem> Warnings { get; init; } = [];
    public IReadOnlyList<PerforceReportItem> Errors { get; init; } = [];
}

/// <summary>Stable connection-test result codes. Shared with the Desktop localization keys.</summary>
public static class PerforceErrorCodes
{
    public const string Connected = "Connected";
    public const string P4ExecutableNotFound = "P4ExecutableNotFound";
    public const string P4ExecutableInvalid = "P4ExecutableInvalid";
    public const string P4ConfigMissing = "P4ConfigMissing";
    public const string MissingPort = "MissingPort";
    public const string MissingClient = "MissingClient";
    public const string MissingUser = "MissingUser";
    public const string ServerUnavailable = "ServerUnavailable";
    public const string Timeout = "Timeout";
    public const string SSLTrustRequired = "SSLTrustRequired";
    public const string LoginRequired = "LoginRequired";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string ClientNotFound = "ClientNotFound";
    public const string ClientHostMismatch = "ClientHostMismatch";
    public const string ClientRootMismatch = "ClientRootMismatch";
    public const string WorkspaceNotMapped = "WorkspaceNotMapped";
    public const string WorkspaceOutsideClientRoot = "WorkspaceOutsideClientRoot";
    public const string Unknown = "Unknown";
}

/// <summary>
/// Orchestrates the read-only Perforce connection-test sequence (version, info, login status,
/// client spec, workspace mapping) and maps each step to a stable, non-sensitive diagnostic.
/// </summary>
public static class PerforceConnectionTester
{
    public static async Task<PerforceConnectionReport> TestAsync(
        IPerforceCommandRunner runner,
        PerforceTestRequest request,
        CancellationToken ct)
    {
        var diag = new PerforceReportDiagnostics(TimeoutSeconds: request.TimeoutSeconds);
        var identity = new PerforceReportIdentity(
            Charset: NullIfEmpty(request.Charset),
            ConnectionMode: request.ConnectionMode);

        if (request.ConnectionMode == SourceControlConnectionModes.Manual
            && string.IsNullOrWhiteSpace(request.Port))
            return Fail(PerforceErrorCodes.MissingPort, "A Perforce server address (P4PORT) is required.", diag, identity);

        // 1) p4 executable present and runnable.
        var version = await runner.RunAsync(["-V"], null, ct).ConfigureAwait(false);
        if (version.ExecutableMissing)
            return Fail(PerforceErrorCodes.P4ExecutableNotFound, "p4 was not found in the server environment.", diag, identity);
        if (!version.Ok)
            return Fail(PerforceErrorCodes.P4ExecutableInvalid, "The p4 executable could not be run.", diag, identity);
        diag = diag with { P4Version = FirstNonEmptyLine(version.StdOut) };

        var globals = BuildGlobals(request);

        // 2) p4 info — server reachability + identity.
        var info = await runner.RunAsync([.. globals, "info"], null, ct).ConfigureAwait(false);
        if (info.TimedOut)
            return Fail(PerforceErrorCodes.Timeout, "The server did not respond before the timeout.", diag, identity);

        var combined = info.StdOut + "\n" + info.StdErr;
        if (LooksLikeSslTrust(combined))
            return Fail(PerforceErrorCodes.SSLTrustRequired, "The server requires trust confirmation.", diag, identity);
        if (!info.Ok || LooksLikeUnreachable(combined))
        {
            if (request.ConnectionMode == SourceControlConnectionModes.P4Config && string.IsNullOrWhiteSpace(request.Port))
                return Fail(PerforceErrorCodes.P4ConfigMissing, "No usable P4CONFIG connection was found.", diag, identity);
            return Fail(PerforceErrorCodes.ServerUnavailable, "The Perforce server could not be reached.", diag, identity);
        }

        var fields = ParseTaggedish(info.StdOut);
        var server = Get(fields, "Server address") ?? NullIfEmpty(request.Port);
        var user = Get(fields, "User name") ?? NullIfEmpty(request.User);
        var client = Get(fields, "Client name") ?? NullIfEmpty(request.Client);
        var clientRoot = Get(fields, "Client root");
        var clientUnknown = string.Equals(client, "*unknown*", StringComparison.OrdinalIgnoreCase)
            || (clientRoot != null && clientRoot.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        identity = identity with { ServerAddress = server, User = user, Client = clientUnknown ? null : client };

        // Required-parameter checks once the server answered.
        if (string.IsNullOrWhiteSpace(user))
            return Fail(PerforceErrorCodes.MissingUser, "A Perforce user (P4USER) is required.", diag, identity);
        if (string.IsNullOrWhiteSpace(client) || clientUnknown)
            return Fail(PerforceErrorCodes.MissingClient, "A Perforce client/workspace (P4CLIENT) is required.", diag, identity, server: server, user: user);

        // 3) login status (and one transient login attempt when a password is supplied).
        var auth = await EvaluateLoginAsync(runner, globals, request, ct).ConfigureAwait(false);
        if (auth.Code == PerforceErrorCodes.AuthenticationFailed)
            return Fail(PerforceErrorCodes.AuthenticationFailed, "Authentication failed.", diag, identity, auth: auth.Auth);
        if (auth.Code == PerforceErrorCodes.LoginRequired)
            return new PerforceConnectionReport
            {
                Status = SourceControlStatuses.LoginRequired,
                Code = PerforceErrorCodes.LoginRequired,
                Summary = $"Perforce server {server} is reachable, but no valid ticket was found for {user}.",
                FallbackText = "Perforce server is reachable, but no valid ticket was found for this user.",
                Identity = identity,
                Authentication = auth.Auth,
                Diagnostics = diag
            };

        // 4) client spec — root / alt-roots.
        var spec = await runner.RunAsync([.. globals, "client", "-o", client!], null, ct).ConfigureAwait(false);
        if (!spec.Ok && spec.TimedOut)
            return Fail(PerforceErrorCodes.Timeout, "The server did not respond before the timeout.", diag, identity, auth: auth.Auth);
        var specFields = ParseTaggedish(spec.StdOut);
        var root = Get(specFields, "Root") ?? clientRoot;
        var altRoots = ParseAltRoots(Get(specFields, "AltRoots"));
        if (string.IsNullOrWhiteSpace(root))
            return Fail(PerforceErrorCodes.ClientNotFound, "The Perforce client/workspace was not found.", diag, identity, auth: auth.Auth);

        // 5) workspace mapping — root containment first, then p4 view mapping.
        var mappingOk = IsInsideAny(request.WorkspacePath, root, altRoots);
        var workspace = new PerforceReportWorkspace(
            WorkspacePath: NullIfEmpty(request.WorkspacePath),
            ClientRoot: root,
            AltRoots: altRoots,
            MappingOk: mappingOk);

        if (!mappingOk)
            return new PerforceConnectionReport
            {
                Status = SourceControlStatuses.Error,
                Code = PerforceErrorCodes.WorkspaceOutsideClientRoot,
                Summary = $"Client {client} exists, but the current workspace is outside its client root.",
                FallbackText = "The current workspace is outside the client root or view.",
                Identity = identity,
                Workspace = workspace,
                Authentication = auth.Auth,
                Diagnostics = diag,
                Errors = [new PerforceReportItem(PerforceErrorCodes.WorkspaceOutsideClientRoot, "The current workspace is outside the client root or view.")]
            };

        var where = await runner.RunAsync([.. globals, "where", request.WorkspacePath], null, ct).ConfigureAwait(false);
        if (where.TimedOut)
            return Fail(
                PerforceErrorCodes.Timeout,
                "The server did not respond before the timeout.",
                diag,
                identity,
                auth: auth.Auth,
                workspace: workspace with { MappingOk = false });

        if (!where.Ok)
        {
            var whereText = where.StdOut + "\n" + where.StdErr;
            var code = ClassifyWhereFailure(whereText);
            var fallback = WhereFailureFallback(code);
            return Fail(
                code,
                fallback,
                diag,
                identity,
                auth: auth.Auth,
                workspace: workspace with { MappingOk = false });
        }

        return new PerforceConnectionReport
        {
            Status = SourceControlStatuses.Connected,
            Code = PerforceErrorCodes.Connected,
            Summary = $"Connected to {server} as {user} using client {client}. Workspace path is inside the client root and view.",
            FallbackText = $"Connected to {server} as {user} using client {client}.",
            Identity = identity,
            Workspace = workspace,
            Authentication = auth.Auth,
            Diagnostics = diag
        };
    }

    private static async Task<(string Code, PerforceReportAuth Auth)> EvaluateLoginAsync(
        IPerforceCommandRunner runner,
        IReadOnlyList<string> globals,
        PerforceTestRequest request,
        CancellationToken ct)
    {
        var status = await runner.RunAsync([.. globals, "login", "-s"], null, ct).ConfigureAwait(false);
        if (status.Ok)
            return (PerforceErrorCodes.Connected, new PerforceReportAuth("valid", false, FirstNonEmptyLine(status.StdOut)));

        // No valid ticket. Try a one-shot login if a transient password was supplied.
        if (!string.IsNullOrEmpty(request.Password))
        {
            var login = await runner.RunAsync([.. globals, "login"], request.Password, ct).ConfigureAwait(false);
            if (login.Ok)
            {
                var recheck = await runner.RunAsync([.. globals, "login", "-s"], null, ct).ConfigureAwait(false);
                if (recheck.Ok)
                    return (PerforceErrorCodes.Connected, new PerforceReportAuth("valid", false, FirstNonEmptyLine(recheck.StdOut)));
            }
            return (PerforceErrorCodes.AuthenticationFailed, new PerforceReportAuth("invalid", true, null));
        }

        return (PerforceErrorCodes.LoginRequired, new PerforceReportAuth("none", true, null));
    }

    private static List<string> BuildGlobals(PerforceTestRequest r)
    {
        var g = new List<string>();
        if (r.ConnectionMode == SourceControlConnectionModes.Manual)
        {
            AddOption(g, "-p", r.Port);
            AddOption(g, "-c", r.Client);
            AddOption(g, "-u", r.User);
        }
        AddOption(g, "-C", r.Charset);
        return g;
    }

    private static void AddOption(List<string> args, string flag, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        args.Add(flag);
        args.Add(value.Trim());
    }

    private static PerforceConnectionReport Fail(
        string code,
        string fallback,
        PerforceReportDiagnostics diag,
        PerforceReportIdentity identity,
        string? server = null,
        string? user = null,
        PerforceReportAuth? auth = null,
        PerforceReportWorkspace? workspace = null)
    {
        _ = server;
        _ = user;

        return new PerforceConnectionReport
        {
            Status = SourceControlStatuses.Error,
            Code = code,
            Summary = fallback,
            FallbackText = fallback,
            Identity = identity,
            Workspace = workspace ?? new PerforceReportWorkspace(),
            Authentication = auth ?? new PerforceReportAuth(),
            Diagnostics = diag with { ErrorCode = code },
            Errors = [new PerforceReportItem(code, fallback)]
        };
    }

    private static bool LooksLikeSslTrust(string text) =>
        text.Contains("trust", StringComparison.OrdinalIgnoreCase)
        && (text.Contains("ssl", StringComparison.OrdinalIgnoreCase)
            || text.Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authenticity", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeUnreachable(string text) =>
        text.Contains("connect to server failed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("TCP connect", StringComparison.OrdinalIgnoreCase)
        || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
        || text.Contains("host unknown", StringComparison.OrdinalIgnoreCase)
        || text.Contains("couldn't connect", StringComparison.OrdinalIgnoreCase);

    private static string ClassifyWhereFailure(string text)
    {
        if (LooksLikeClientHostMismatch(text))
            return PerforceErrorCodes.ClientHostMismatch;
        if (LooksLikeClientRootMismatch(text))
            return PerforceErrorCodes.ClientRootMismatch;
        if (LooksLikeWorkspaceNotMapped(text))
            return PerforceErrorCodes.WorkspaceNotMapped;
        return PerforceErrorCodes.WorkspaceNotMapped;
    }

    private static string WhereFailureFallback(string code) => code switch
    {
        PerforceErrorCodes.ClientHostMismatch => "The Perforce client is restricted to a different host.",
        PerforceErrorCodes.ClientRootMismatch => "The current workspace path does not match the Perforce client root.",
        PerforceErrorCodes.WorkspaceNotMapped => "The current workspace path is not mapped by the Perforce client view.",
        _ => "The current workspace path is not mapped by the Perforce client view."
    };

    private static bool LooksLikeClientHostMismatch(string text) =>
        text.Contains("can only be used from host", StringComparison.OrdinalIgnoreCase)
        || text.Contains("host mismatch", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeClientRootMismatch(string text) =>
        text.Contains("not under client", StringComparison.OrdinalIgnoreCase)
        || text.Contains("not under client's root", StringComparison.OrdinalIgnoreCase)
        || text.Contains("client root", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWorkspaceNotMapped(string text) =>
        text.Contains("not in client view", StringComparison.OrdinalIgnoreCase)
        || text.Contains("not mapped", StringComparison.OrdinalIgnoreCase)
        || text.Contains("no such file", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseTaggedish(string stdout)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var idx = line.IndexOf(": ", StringComparison.Ordinal);
            if (idx <= 0)
                continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 2)..].Trim();
            if (key.Length > 0 && !map.ContainsKey(key))
                map[key] = value;
        }
        return map;
    }

    private static IReadOnlyList<string> ParseAltRoots(string? altRoots)
    {
        if (string.IsNullOrWhiteSpace(altRoots))
            return [];
        return altRoots
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static bool IsInsideAny(string workspacePath, string root, IReadOnlyList<string> altRoots)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            return false;
        if (IsInside(workspacePath, root))
            return true;
        return altRoots.Any(alt => IsInside(workspacePath, alt));
    }

    private static bool IsInside(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;
        try
        {
            var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(c, r, cmp))
                return true;
            return c.StartsWith(r + Path.DirectorySeparatorChar, cmp);
        }
        catch
        {
            return false;
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmptyLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                return line;
        }
        return null;
    }
}

/// <summary>Production <see cref="IPerforceCommandRunner"/> backed by <see cref="PerforceProcessRunner"/>.</summary>
public sealed class DefaultPerforceCommandRunner(
    string executable,
    string workingDirectory,
    TimeSpan timeout,
    IDictionary<string, string>? extraEnv = null,
    ILogger? logger = null) : IPerforceCommandRunner
{
    public async Task<PerforceCommandResult> RunAsync(IReadOnlyList<string> args, string? stdinInput, CancellationToken ct)
    {
        try
        {
            var r = await PerforceProcessRunner
                .RunAsync(executable, workingDirectory, args, timeout, ct, extraEnv, stdinInput, logger)
                .ConfigureAwait(false);
            return new PerforceCommandResult(r.ExitCode, r.StdOut, r.StdErr, ExecutableMissing: false, TimedOut: false);
        }
        catch (PerforceProcessTimeoutException)
        {
            return new PerforceCommandResult(-1, string.Empty, string.Empty, ExecutableMissing: false, TimedOut: true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Executable not found / failed to start.
            return new PerforceCommandResult(-1, string.Empty, ex.Message, ExecutableMissing: true, TimedOut: false);
        }
    }
}
