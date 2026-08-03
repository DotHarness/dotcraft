using System.Text;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;

const string ClientName = "dotcraft-agent-profile-sample";
const string ClientVersion = "0.1.0";
const string WorkspaceSource = "workspace";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var ct = cts.Token;

var options = SampleOptions.Parse(args);
if (options.ShowHelp || string.IsNullOrWhiteSpace(options.WorkspacePath))
{
    PrintUsage();
    return options.ShowHelp ? 0 : 1;
}

try
{
    return await RunAsync(options, ct);
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

async Task<int> RunAsync(SampleOptions sampleOptions, CancellationToken cancellationToken)
{
    var workspacePath = Path.GetFullPath(sampleOptions.WorkspacePath!);
    var profileId = sampleOptions.ProfileId;
    Console.WriteLine($"Connecting to workspace AppServer: {workspacePath}");
    await using var client = await DotCraftClient.ConnectLocalAsync(
        workspacePath,
        new DotCraftLocalOptions
        {
            ClientName = ClientName,
            ClientTitle = "Agent Profile Thread Sample",
            ClientVersion = ClientVersion,
            Executable = sampleOptions.Executable,
            ApprovalHandler = HandleApprovalAsync
        },
        cancellationToken);

    RequireAgentProfileManagement(client);
    Console.WriteLine($"Connected to {client.ServerInfo.Name} {client.ServerInfo.Version}");

    await EnsureProfileAsync(client, profileId, sampleOptions.OverwriteProfile, cancellationToken);

    var thread = await client.Threads.StartAsync(
        new ThreadStartParams
        {
            Identity = new SessionIdentity
            {
                ChannelName = ClientName,
                UserId = Environment.UserName,
                WorkspacePath = workspacePath
            },
            DisplayName = sampleOptions.DisplayName ?? $"Agent Profile Smoke: {profileId}",
            HistoryMode = "server",
            Config = new ThreadConfiguration { AgentProfileId = profileId }
        },
        cancellationToken);

    Console.WriteLine();
    Console.WriteLine($"Created profile-backed thread: {thread.Id}");
    await PrintThreadConfigurationAsync(client, thread.Id, cancellationToken);
    PrintReplHelp();
    await RunReplAsync(client, thread.Id, profileId, cancellationToken);
    return 0;
}

async Task EnsureProfileAsync(
    DotCraftClient client,
    string profileId,
    bool overwrite,
    CancellationToken cancellationToken)
{
    var list = await client.Wire.AgentProfilesListAsync(
        new AgentProfileListParams { IncludeInvalid = true }, cancellationToken);
    var existing = list.Profiles.IsSet
        ? list.Profiles.Value?.FirstOrDefault(profile => profile.Id.IsSet && string.Equals(profile.Id.Value, profileId, StringComparison.OrdinalIgnoreCase))
        : null;
    if (existing is not null && !overwrite)
    {
        Console.WriteLine($"Using existing profile '{profileId}' from source '{OptionalValue(existing.Source) ?? "unknown"}'.");
        var read = await client.Wire.AgentProfilesReadAsync(new AgentProfileReadParams { Id = profileId }, cancellationToken);
        var profile = read.Profile.IsSet ? read.Profile.Value : null;
        PrintProfileSummary(profile);
        if (profile?.Valid.IsSet != true || profile.Valid.Value != true)
            throw new InvalidOperationException("Existing profile is invalid. Fix it or rerun with --overwrite-profile.");
        return;
    }

    if (existing is not null && overwrite)
    {
        Console.WriteLine($"Overwriting workspace profile '{profileId}' with the smoke profile.");
    }
    else
    {
        Console.WriteLine($"Creating workspace profile '{profileId}'.");
    }

    var rawContent = BuildSmokeProfile(profileId);
    var validation = await client.Wire.AgentProfilesValidateAsync(
        new AgentProfileValidateParams { Source = WorkspaceSource, RawContent = rawContent }, cancellationToken);
    if (!validation.Valid.IsSet || !validation.Valid.Value)
    {
        Console.Error.WriteLine("Default smoke profile failed validation:");
        PrintDiagnostics(validation.Diagnostics.IsSet ? validation.Diagnostics.Value : null);
        throw new InvalidOperationException("Default smoke profile is invalid.");
    }

    var upsert = await client.Wire.AgentProfilesUpsertAsync(
        new AgentProfileUpsertParams { Id = profileId, Source = WorkspaceSource, RawContent = rawContent }, cancellationToken);
    PrintProfileSummary(upsert.Profile.IsSet ? upsert.Profile.Value : null);
}

async Task RunReplAsync(
    DotCraftClient client,
    string threadId,
    string profileId,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        Console.WriteLine();
        Console.Write("profile> ");
        var input = Console.ReadLine();
        if (input == null)
            break;

        var text = input.Trim();
        if (text.Length == 0)
            continue;

        if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (string.Equals(text, "/help", StringComparison.OrdinalIgnoreCase))
        {
            PrintReplHelp();
            continue;
        }

        if (string.Equals(text, "/read", StringComparison.OrdinalIgnoreCase))
        {
            await PrintThreadConfigurationAsync(client, threadId, cancellationToken);
            continue;
        }

        if (string.Equals(text, "/profile", StringComparison.OrdinalIgnoreCase))
        {
            var read = await client.Wire.AgentProfilesReadAsync(new AgentProfileReadParams { Id = profileId }, cancellationToken);
            PrintProfileSummary(read.Profile.IsSet ? read.Profile.Value : null);
            continue;
        }

        if (string.Equals(text, "/refresh", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshThreadAsync(client, threadId, cancellationToken);
            continue;
        }

        await RunTurnAsync(client, threadId, text, cancellationToken);
    }
}

async Task RunTurnAsync(
    DotCraftClient client,
    string threadId,
    string text,
    CancellationToken cancellationToken)
{
    var thread = await client.Threads.ResumeAsync(new ThreadResumeParams { ThreadId = threadId }, cancellationToken);
    Console.WriteLine("Assistant:");
    var sawDelta = false;
    await foreach (var runEvent in thread.RunStreamedAsync(
                       text,
                       new RunOptions
                       {
                           EnqueueIfBusy = true,
                           ThrowOnFailure = false
                       },
                       cancellationToken))
    {
        if (runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta &&
            runEvent is DotCraftRunEvent<ItemDeltaNotification> deltaEvent)
        {
            var delta = deltaEvent.Params.Delta;
            if (!string.IsNullOrEmpty(delta))
            {
                sawDelta = true;
                Console.Write(delta);
            }
        }
        else if (runEvent.Type == DotCraftRunEventTypes.ToolArgumentsDelta &&
                 runEvent is DotCraftRunEvent<ItemDeltaNotification> toolEvent)
        {
            var toolName = toolEvent.Params.ToolName;
            if (!string.IsNullOrWhiteSpace(toolName))
                Console.WriteLine($"{Environment.NewLine}[tool args] {toolName}");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.ApprovalResolved)
        {
            Console.WriteLine($"{Environment.NewLine}[approval resolved]");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.Failed &&
                 runEvent is DotCraftRunEvent<TurnNotification> failed)
        {
            Console.WriteLine();
            Console.WriteLine($"[turn failed] {failed.Params.Error ?? failed.Params.Turn.Error ?? "unknown error"}");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.Cancelled &&
                 runEvent is DotCraftRunEvent<TurnNotification> cancelled)
        {
            Console.WriteLine();
            Console.WriteLine($"[turn cancelled] {cancelled.Params.Reason ?? "cancelled"}");
        }
    }

    if (sawDelta)
        Console.WriteLine();
}

async Task RefreshThreadAsync(
    DotCraftClient client,
    string threadId,
    CancellationToken cancellationToken)
{
    var result = await client.Wire.AgentProfilesRefreshThreadAsync(
        new AgentProfileRefreshThreadParams { ThreadId = threadId }, cancellationToken);

    Console.WriteLine("Refresh result:");
    Console.WriteLine($"  wasStale: {(result.WasStale.IsSet ? result.WasStale.Value.ToString() : "unknown")}");
    if (result.Profile.IsSet && result.Profile.Value is { } profile)
    {
        Console.WriteLine($"  profile: {OptionalValue(profile.Id) ?? "(unknown)"}");
        Console.WriteLine($"  source: {OptionalValue(profile.Source) ?? "(unknown)"}");
        Console.WriteLine($"  fingerprint: {OptionalValue(profile.Fingerprint) ?? "(none)"}");
    }

    await PrintThreadConfigurationAsync(client, threadId, cancellationToken);
}

async Task PrintThreadConfigurationAsync(
    DotCraftClient client,
    string threadId,
    CancellationToken cancellationToken)
{
    var read = await client.Threads.ReadAsync(threadId, cancellationToken: cancellationToken);
    var config = read.Thread.Configuration;
    if (config is null)
    {
        Console.WriteLine("Thread configuration was not returned.");
        return;
    }

    Console.WriteLine("Thread configuration:");
    Console.WriteLine($"  agentProfileId: {OptionalValue(config.AgentProfileId) ?? "(none)"}");
    Console.WriteLine($"  agentProfileSource: {OptionalValue(config.AgentProfileSource) ?? "(none)"}");
    Console.WriteLine($"  agentProfileFingerprint: {OptionalValue(config.AgentProfileFingerprint) ?? "(none)"}");
    Console.WriteLine($"  hasRoleInstructions: {!string.IsNullOrWhiteSpace(OptionalValue(config.RoleInstructions))}");
    Console.WriteLine($"  toolPolicy.deny: {FormatNullableOptionalStrings(config.ToolPolicy.IsSet ? config.ToolPolicy.Value?.Deny : default)}");
    Console.WriteLine($"  legacy toolDenyList: {FormatOptionalStrings(config.ToolDenyList)}");
}

Task<ApprovalResponseResult> HandleApprovalAsync(ApprovalRequestParams request, CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine("Approval requested:");
    Console.WriteLine(JsonSerializer.Serialize(request, AppServerContractJson.Options));
    Console.Write("Accept this request? Type 'y' to accept, anything else to decline: ");
    var answer = Console.ReadLine();
    return Task.FromResult(string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
        ? ApprovalResponses.Accept
        : ApprovalResponses.Decline);
}

void RequireAgentProfileManagement(DotCraftClient client)
{
    if (client.Capabilities.ExtensionData?.TryGetValue("agentProfileManagement", out var value) == true
        && value.ValueKind == JsonValueKind.True)
    {
        return;
    }

    throw new InvalidOperationException("Connected AppServer does not advertise agentProfileManagement.");
}

static T? OptionalValue<T>(Optional<T> value) => value.IsSet ? value.Value : default;

static string FormatOptionalStrings(Optional<IReadOnlyList<string>?> value) =>
    value.IsSet && value.Value is { Count: > 0 } values ? $"[{string.Join(", ", values)}]" : "[]";

static string FormatNullableOptionalStrings(Optional<IReadOnlyList<string>?>? value) =>
    value.HasValue ? FormatOptionalStrings(value.Value) : "[]";

static string FormatRequiredOptionalStrings(Optional<IReadOnlyList<string>> value) =>
    value.IsSet && value.Value is { Count: > 0 } values ? $"[{string.Join(", ", values)}]" : "[]";

void PrintProfileSummary(AgentProfileEntry? profile)
{
    if (profile is null)
    {
        Console.WriteLine("Profile was not returned.");
        return;
    }

    Console.WriteLine("Profile:");
    Console.WriteLine($"  id: {OptionalValue(profile.Id) ?? "(none)"}");
    Console.WriteLine($"  source: {OptionalValue(profile.Source) ?? "(none)"}");
    Console.WriteLine($"  valid: {(profile.Valid.IsSet ? profile.Valid.Value.ToString() : "(unknown)")}");
    Console.WriteLine($"  readOnly: {(profile.ReadOnly.IsSet ? profile.ReadOnly.Value.ToString() : "(unknown)")}");
    Console.WriteLine($"  fingerprint: {OptionalValue(profile.Fingerprint) ?? "(none)"}");
    Console.WriteLine($"  staleThreadIds: {FormatRequiredOptionalStrings(profile.StaleThreadIds)}");
    PrintDiagnostics(profile.Diagnostics.IsSet ? profile.Diagnostics.Value : null);
}

void PrintDiagnostics(IReadOnlyList<AgentProfileDiagnostic>? diagnostics)
{
    if (diagnostics is not { Count: > 0 })
        return;
    Console.WriteLine("  diagnostics:");
    foreach (var diagnostic in diagnostics)
        Console.WriteLine($"    [{OptionalValue(diagnostic.Severity) ?? "diagnostic"}] {OptionalValue(diagnostic.Code) ?? "unknown"}: {OptionalValue(diagnostic.Message) ?? "(no message)"}");
}

static string BuildSmokeProfile(string profileId) =>
    $$"""
      ---
      name: {{profileId}}
      description: Smoke test reviewer with read-only intent.
      model: inherit
      tools:
        deny: [WriteFile, EditFile, Exec, WriteStdin]
      permissions:
        approvalPolicy: default
      ---

      You are a smoke-test reviewer. Do not edit files. Report risks and missing tests only.
      Always mention PROFILE_SMOKE_REVIEWER when explaining your role.
      """;

static void PrintReplHelp()
{
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  /read     Print the current thread configuration summary.");
    Console.WriteLine("  /profile  Read and print the current profile summary.");
    Console.WriteLine("  /refresh  Refresh this thread from the current profile.");
    Console.WriteLine("  /help     Show this help.");
    Console.WriteLine("  /exit     Quit.");
    Console.WriteLine("Any other input starts a turn on the profile-backed thread.");
}

static void PrintUsage()
{
    Console.WriteLine("""
        AgentProfileThreadSample

        Usage:
          dotnet run --project sdk/dotnet/samples/AgentProfileThreadSample -- <workspacePath> [options]

        Options:
          --profile-id <id>       Agent Profile id to use. Default: smoke-reviewer
          --overwrite-profile     Replace the workspace profile with the sample smoke profile.
          --display-name <name>   Thread display name.
          --executable <path>     DotCraft executable used when starting the local Hub.
          --help                  Show this help.

        REPL commands:
          /read, /profile, /refresh, /help, /exit
        """);
}

sealed class SampleOptions
{
    private const string DefaultProfileId = "smoke-reviewer";

    public string? WorkspacePath { get; private init; }

    public string ProfileId { get; private init; } = DefaultProfileId;

    public bool OverwriteProfile { get; private init; }

    public string? DisplayName { get; private init; }

    public string? Executable { get; private init; }

    public bool ShowHelp { get; private init; }

    public static SampleOptions Parse(string[] args)
    {
        string? workspacePath = null;
        var profileId = DefaultProfileId;
        var overwriteProfile = false;
        string? displayName = null;
        string? executable = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--profile-id" when i + 1 < args.Length:
                    profileId = args[++i];
                    break;
                case "--overwrite-profile":
                    overwriteProfile = true;
                    break;
                case "--display-name" when i + 1 < args.Length:
                    displayName = args[++i];
                    break;
                case "--executable" when i + 1 < args.Length:
                    executable = args[++i];
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {arg}");
                    workspacePath ??= arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("--profile-id cannot be empty.");

        return new SampleOptions
        {
            WorkspacePath = workspacePath,
            ProfileId = profileId.Trim(),
            OverwriteProfile = overwriteProfile,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            Executable = string.IsNullOrWhiteSpace(executable) ? null : executable,
            ShowHelp = showHelp
        };
    }
}
