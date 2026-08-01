using System.Text;
using System.Text.Json;
using DotCraft.Sdk.AppServer;
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
        new DotCraftLocalClientOptions
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
        new DotCraftThreadStartRequest(
            new SessionIdentity(
                ChannelName: ClientName,
                UserId: Environment.UserName,
                WorkspacePath: workspacePath),
            DisplayName: sampleOptions.DisplayName ?? $"Agent Profile Smoke: {profileId}",
            HistoryMode: "server",
            Config: new { agentProfileId = profileId }),
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
    var list = await client.RequestAsync("agent/profiles/list", new { includeInvalid = true }, cancellationToken);
    var existing = FindProfile(list, profileId);
    if (existing.HasValue && !overwrite)
    {
        Console.WriteLine($"Using existing profile '{profileId}' from source '{ReadString(existing.Value, "source") ?? "unknown"}'.");
        var read = await client.RequestAsync("agent/profiles/read", new { id = profileId }, cancellationToken);
        PrintProfileSummary(read);
        if (!IsProfileValid(read))
            throw new InvalidOperationException("Existing profile is invalid. Fix it or rerun with --overwrite-profile.");
        return;
    }

    if (existing.HasValue && overwrite)
    {
        Console.WriteLine($"Overwriting workspace profile '{profileId}' with the smoke profile.");
    }
    else
    {
        Console.WriteLine($"Creating workspace profile '{profileId}'.");
    }

    var rawContent = BuildSmokeProfile(profileId);
    var validation = await client.RequestAsync(
        "agent/profiles/validate",
        new { source = WorkspaceSource, rawContent },
        cancellationToken);
    if (!IsTrue(validation, "valid"))
    {
        Console.Error.WriteLine("Default smoke profile failed validation:");
        PrintDiagnostics(validation);
        throw new InvalidOperationException("Default smoke profile is invalid.");
    }

    var upsert = await client.RequestAsync(
        "agent/profiles/upsert",
        new { id = profileId, source = WorkspaceSource, rawContent },
        cancellationToken);
    PrintProfileSummary(upsert);
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
            var profile = await client.RequestAsync("agent/profiles/read", new { id = profileId }, cancellationToken);
            PrintProfileSummary(profile);
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
    var thread = await client.Threads.ResumeAsync(new DotCraftThreadResumeRequest(threadId), cancellationToken);
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
        if (runEvent.Type == DotCraftRunEventTypes.AgentMessageDelta)
        {
            var delta = ReadString(runEvent.Params, "delta")
                        ?? ReadString(runEvent.Params, "text")
                        ?? ReadNestedString(runEvent.Params, "delta", "text");
            if (!string.IsNullOrEmpty(delta))
            {
                sawDelta = true;
                Console.Write(delta);
            }
        }
        else if (runEvent.Type == DotCraftRunEventTypes.ToolArgumentsDelta)
        {
            var toolName = ReadString(runEvent.Params, "toolName")
                           ?? ReadNestedString(runEvent.Params, "toolCall", "name")
                           ?? ReadString(runEvent.Params, "name");
            if (!string.IsNullOrWhiteSpace(toolName))
                Console.WriteLine($"{Environment.NewLine}[tool args] {toolName}");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.ApprovalResolved)
        {
            Console.WriteLine($"{Environment.NewLine}[approval resolved]");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.Failed)
        {
            Console.WriteLine();
            Console.WriteLine($"[turn failed] {ExtractTerminalMessage(runEvent.Params) ?? runEvent.Params.GetRawText()}");
        }
        else if (runEvent.Type == DotCraftRunEventTypes.Cancelled)
        {
            Console.WriteLine();
            Console.WriteLine($"[turn cancelled] {ExtractTerminalMessage(runEvent.Params) ?? runEvent.Params.GetRawText()}");
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
    var result = await client.RequestAsync(
        "agent/profiles/refreshThread",
        new { threadId },
        cancellationToken);

    Console.WriteLine("Refresh result:");
    Console.WriteLine($"  wasStale: {ReadBool(result, "wasStale")?.ToString() ?? "unknown"}");
    if (result.TryGetProperty("profile", out var profile))
    {
        Console.WriteLine($"  profile: {ReadString(profile, "id") ?? "(unknown)"}");
        Console.WriteLine($"  source: {ReadString(profile, "source") ?? "(unknown)"}");
        Console.WriteLine($"  fingerprint: {ReadString(profile, "fingerprint") ?? "(none)"}");
    }

    await PrintThreadConfigurationAsync(client, threadId, cancellationToken);
}

async Task PrintThreadConfigurationAsync(
    DotCraftClient client,
    string threadId,
    CancellationToken cancellationToken)
{
    var read = await client.Threads.ReadAsync(threadId, cancellationToken: cancellationToken);
    var thread = read.Thread;
    if (!thread.TryGetProperty("configuration", out var config) || config.ValueKind != JsonValueKind.Object)
    {
        Console.WriteLine("Thread configuration was not returned.");
        return;
    }

    Console.WriteLine("Thread configuration:");
    Console.WriteLine($"  agentProfileId: {ReadString(config, "agentProfileId") ?? "(none)"}");
    Console.WriteLine($"  agentProfileSource: {ReadString(config, "agentProfileSource") ?? "(none)"}");
    Console.WriteLine($"  agentProfileFingerprint: {ReadString(config, "agentProfileFingerprint") ?? "(none)"}");
    Console.WriteLine($"  hasRoleInstructions: {HasNonEmptyString(config, "roleInstructions")}");
    Console.WriteLine($"  toolPolicy.deny: {ReadNestedStringArray(config, "toolPolicy", "deny")}");
    Console.WriteLine($"  legacy toolDenyList: {ReadStringArray(config, "toolDenyList")}");
}

Task<ApprovalDecision> HandleApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine("Approval requested:");
    Console.WriteLine(FormatJson(request.Raw));
    Console.Write("Accept this request? Type 'y' to accept, anything else to decline: ");
    var answer = Console.ReadLine();
    return Task.FromResult(string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
        ? ApprovalDecision.Accept
        : ApprovalDecision.Decline);
}

void RequireAgentProfileManagement(DotCraftClient client)
{
    if (client.Capabilities.Raw.TryGetProperty("agentProfileManagement", out var value)
        && value.ValueKind == JsonValueKind.True)
    {
        return;
    }

    throw new InvalidOperationException("Connected AppServer does not advertise agentProfileManagement.");
}

JsonElement? FindProfile(JsonElement listResult, string profileId)
{
    if (!listResult.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array)
        return null;

    foreach (var profile in profiles.EnumerateArray())
    {
        if (string.Equals(ReadString(profile, "id"), profileId, StringComparison.OrdinalIgnoreCase))
            return profile.Clone();
    }

    return null;
}

void PrintProfileSummary(JsonElement result)
{
    var profile = result.TryGetProperty("profile", out var profileElement)
        ? profileElement
        : result;
    Console.WriteLine("Profile:");
    Console.WriteLine($"  id: {ReadString(profile, "id") ?? "(none)"}");
    Console.WriteLine($"  source: {ReadString(profile, "source") ?? "(none)"}");
    Console.WriteLine($"  valid: {ReadBool(profile, "valid")?.ToString() ?? "(unknown)"}");
    Console.WriteLine($"  readOnly: {ReadBool(profile, "readOnly")?.ToString() ?? "(unknown)"}");
    Console.WriteLine($"  fingerprint: {ReadString(profile, "fingerprint") ?? "(none)"}");
    Console.WriteLine($"  staleThreadIds: {ReadStringArray(profile, "staleThreadIds")}");
    PrintDiagnostics(profile);
}

void PrintDiagnostics(JsonElement source)
{
    if (!source.TryGetProperty("diagnostics", out var diagnostics) || diagnostics.ValueKind != JsonValueKind.Array)
        return;

    var items = diagnostics.EnumerateArray().ToArray();
    if (items.Length == 0)
        return;

    Console.WriteLine("  diagnostics:");
    foreach (var diagnostic in items)
    {
        var severity = ReadString(diagnostic, "severity") ?? "diagnostic";
        var code = ReadString(diagnostic, "code") ?? "unknown";
        var message = ReadString(diagnostic, "message") ?? diagnostic.GetRawText();
        Console.WriteLine($"    [{severity}] {code}: {message}");
    }
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

static string? ExtractTerminalMessage(JsonElement value) =>
    ReadString(value, "message")
    ?? ReadNestedString(value, "error", "message")
    ?? ReadNestedString(value, "turn", "error", "message");

static bool IsTrue(JsonElement value, string property) =>
    value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.True;

static bool IsProfileValid(JsonElement value)
{
    var profile = value.TryGetProperty("profile", out var profileElement)
        ? profileElement
        : value;
    return ReadBool(profile, "valid") == true;
}

static bool? ReadBool(JsonElement value, string property)
{
    if (!value.TryGetProperty(property, out var element))
        return null;
    return element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}

static bool HasNonEmptyString(JsonElement value, string property) =>
    !string.IsNullOrWhiteSpace(ReadString(value, property));

static string? ReadString(JsonElement value, string property)
{
    if (!value.TryGetProperty(property, out var element))
        return null;
    return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}

static string? ReadNestedString(JsonElement value, params string[] path)
{
    var current = value;
    foreach (var segment in path)
    {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            return null;
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
}

static string ReadStringArray(JsonElement value, string property)
{
    if (!value.TryGetProperty(property, out var element))
        return "[]";
    return FormatStringArray(element);
}

static string ReadNestedStringArray(JsonElement value, string objectProperty, string arrayProperty)
{
    if (!value.TryGetProperty(objectProperty, out var nested) || nested.ValueKind != JsonValueKind.Object)
        return "[]";
    return ReadStringArray(nested, arrayProperty);
}

static string FormatStringArray(JsonElement value)
{
    if (value.ValueKind != JsonValueKind.Array)
        return "[]";
    var items = value.EnumerateArray()
        .Where(item => item.ValueKind == JsonValueKind.String)
        .Select(item => item.GetString())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
    return items.Length == 0 ? "[]" : $"[{string.Join(", ", items)}]";
}

static string FormatJson(JsonElement value) =>
    JsonSerializer.Serialize(value, new JsonSerializerOptions(DotCraftJson.Options)
    {
        WriteIndented = true
    });

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
