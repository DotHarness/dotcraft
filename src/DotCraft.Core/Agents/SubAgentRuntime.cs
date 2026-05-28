using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Security;

namespace DotCraft.Agents;

public interface ISubAgentRuntime
{
    string RuntimeType { get; }

    Task<SubAgentSessionHandle> CreateSessionAsync(
        SubAgentProfile profile,
        SubAgentLaunchContext context,
        CancellationToken cancellationToken);

    Task<SubAgentRunResult> RunAsync(
        SubAgentSessionHandle session,
        SubAgentTaskRequest request,
        ISubAgentEventSink sink,
        CancellationToken cancellationToken);

    Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken);

    Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken);
}

public sealed record SubAgentLaunchContext(
    string WorkspaceRoot,
    string WorkingDirectory,
    string? ProfileName = null,
    IReadOnlyList<string>? ExtraLaunchArgs = null,
    string? ResumeSessionId = null,
    string? ApprovalMode = null,
    IApprovalService? ApprovalService = null,
    ApprovalContext? ApprovalContext = null);

public sealed record SubAgentSessionHandle(
    string RuntimeType,
    string? ProfileName = null,
    object? State = null);

public sealed record SubAgentTaskRequest
{
    public required string Task { get; init; }

    public string? Label { get; init; }

    public string? WorkingDirectory { get; init; }

    public ApprovalContext? ApprovalContext { get; init; }
}

public sealed record SubAgentRunResult
{
    public required string Text { get; init; }

    public bool IsError { get; init; }

    public SubAgentTokenUsage? TokensUsed { get; init; }

    public string? SessionId { get; init; }
}

public sealed record SubAgentTokenUsage(
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens = 0,
    long CacheWriteInputTokens = 0,
    long ReasoningOutputTokens = 0);

public sealed record SubAgentPreparedRun(
    SubAgentProfile Profile,
    ISubAgentRuntime Runtime,
    SubAgentLaunchContext LaunchContext,
    SubAgentTaskRequest Request);

public sealed record SubAgentProfileDiagnostic
{
    public required string Name { get; init; }

    public required string Runtime { get; init; }

    public required string WorkingDirectoryMode { get; init; }

    public bool IsBuiltIn { get; init; }

    public bool Enabled { get; init; }

    public string? Bin { get; init; }

    public string? ResolvedBinary { get; init; }

    public bool BinaryResolved { get; init; }

    public bool RuntimeRegistered { get; init; }

    public bool HiddenFromPrompt { get; init; }

    public string? HiddenReason { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ISubAgentEventSink
{
    void OnInfo(string message);

    void OnProgress(string? currentTool, string? currentToolDisplay = null);

    void OnCompleted(string? summary = null, SubAgentTokenUsage? tokensUsed = null);

    void OnFailed(string? summary = null, SubAgentTokenUsage? tokensUsed = null);
}

public sealed class NullSubAgentEventSink : ISubAgentEventSink
{
    public static readonly NullSubAgentEventSink Instance = new();

    private NullSubAgentEventSink()
    {
    }

    public void OnInfo(string message)
    {
    }

    public void OnProgress(string? currentTool, string? currentToolDisplay = null)
    {
    }

    public void OnCompleted(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
    {
    }

    public void OnFailed(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
    {
    }
}

public sealed class NativeSubAgentRuntime(SubAgentManager subAgentManager) : ISubAgentRuntime
{
    public const string RuntimeTypeName = "native";

    public string RuntimeType => RuntimeTypeName;

    public Task<SubAgentSessionHandle> CreateSessionAsync(
        SubAgentProfile profile,
        SubAgentLaunchContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(
            new SubAgentSessionHandle(
                RuntimeType,
                profile.Name,
                new NativeSessionState(context)));

    public async Task<SubAgentRunResult> RunAsync(
        SubAgentSessionHandle session,
        SubAgentTaskRequest request,
        ISubAgentEventSink sink,
        CancellationToken cancellationToken)
    {
        _ = sink;
        var launchContext = (session.State as NativeSessionState)?.LaunchContext;
        var approvalService = launchContext?.ApprovalService;
        var approvalContext = request.ApprovalContext ?? launchContext?.ApprovalContext;

        // TokenTracker is shared at the turn scope, so this is best-effort metadata only.
        var tokenTracker = TokenTracker.Current;
        var beforeInput = tokenTracker?.SubAgentInputTokens ?? 0;
        var beforeOutput = tokenTracker?.SubAgentOutputTokens ?? 0;
        var beforeCachedInput = tokenTracker?.SubAgentCachedInputTokens ?? 0;
        var beforeCacheWriteInput = tokenTracker?.SubAgentCacheWriteInputTokens ?? 0;
        var beforeReasoningOutput = tokenTracker?.SubAgentReasoningOutputTokens ?? 0;

        var text = await subAgentManager.SpawnAsync(
            request.Task,
            request.Label,
            approvalService,
            approvalContext,
            cancellationToken);

        var afterInput = tokenTracker?.SubAgentInputTokens ?? beforeInput;
        var afterOutput = tokenTracker?.SubAgentOutputTokens ?? beforeOutput;
        var afterCachedInput = tokenTracker?.SubAgentCachedInputTokens ?? beforeCachedInput;
        var afterCacheWriteInput = tokenTracker?.SubAgentCacheWriteInputTokens ?? beforeCacheWriteInput;
        var afterReasoningOutput = tokenTracker?.SubAgentReasoningOutputTokens ?? beforeReasoningOutput;
        var tokensUsed = new SubAgentTokenUsage(
            Math.Max(0, afterInput - beforeInput),
            Math.Max(0, afterOutput - beforeOutput),
            Math.Max(0, afterCachedInput - beforeCachedInput),
            Math.Max(0, afterCacheWriteInput - beforeCacheWriteInput),
            Math.Max(0, afterReasoningOutput - beforeReasoningOutput));
        var isError = text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

        return new SubAgentRunResult
        {
            Text = text,
            IsError = isError,
            TokensUsed = tokensUsed
        };
    }

    public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private sealed record NativeSessionState(SubAgentLaunchContext LaunchContext);
}

public sealed class SubAgentProfileRegistry
{
    private const int DefaultTimeoutSeconds = 300;
    private const int DefaultMaxOutputBytes = 1024 * 1024;

    private readonly IReadOnlyDictionary<string, SubAgentProfile> _profiles;
    private readonly IReadOnlySet<string> _builtInProfileNames;
    private readonly IReadOnlySet<string> _builtInTemplateProfileNames;
    private readonly IReadOnlySet<string> _disabledProfileNames;
    private readonly IReadOnlyList<string> _validationWarnings;

    public SubAgentProfileRegistry(
        IEnumerable<SubAgentProfile>? configuredProfiles,
        IEnumerable<SubAgentProfile>? builtInProfiles,
        IEnumerable<string>? knownRuntimeTypes = null,
        IEnumerable<string>? disabledProfiles = null)
    {
        var map = new Dictionary<string, SubAgentProfile>(StringComparer.OrdinalIgnoreCase);

        var builtInNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtInTemplateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredOverrideNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownBuiltInTemplateNames = new HashSet<string>(
            CreateBuiltInTemplateProfileNames(),
            StringComparer.OrdinalIgnoreCase);
        if (builtInProfiles != null)
        {
            foreach (var profile in builtInProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                    continue;
                builtInNames.Add(profile.Name);
                if (knownBuiltInTemplateNames.Contains(profile.Name))
                    builtInTemplateNames.Add(profile.Name);
                map[profile.Name] = profile.Clone();
            }
        }

        if (configuredProfiles != null)
        {
            foreach (var profile in configuredProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                    continue;
                configuredOverrideNames.Add(profile.Name);
                map[profile.Name] = profile.Clone();
            }
        }

        _profiles = map;
        _builtInProfileNames = builtInNames;
        _builtInTemplateProfileNames = builtInTemplateNames
            .Where(name => !configuredOverrideNames.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _disabledProfileNames = (disabledProfiles ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !string.Equals(name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _validationWarnings = ValidateProfiles(_profiles.Values, knownRuntimeTypes, _builtInTemplateProfileNames);
    }

    public static IReadOnlyList<string> KnownRuntimeTypes =>
    [
        NativeSubAgentRuntime.RuntimeTypeName,
        CliOneshotRuntime.RuntimeTypeName
    ];

    public static IReadOnlyList<string> CreateBuiltInTemplateProfileNames() =>
    [
        "custom-cli-oneshot"
    ];

    public static IReadOnlyList<SubAgentProfile> CreateBuiltInProfiles()
        =>
        [
            new SubAgentProfile
            {
                Name = SubAgentCoordinator.DefaultProfileName,
                Runtime = NativeSubAgentRuntime.RuntimeTypeName,
                WorkingDirectoryMode = "workspace",
                TrustLevel = "trusted"
            },
            new SubAgentProfile
            {
                Name = "codex-cli",
                Runtime = CliOneshotRuntime.RuntimeTypeName,
                Bin = "codex",
                Args =
                [
                    "exec"
                ],
                WorkingDirectoryMode = "workspace",
                InputMode = "arg",
                OutputFormat = "text",
                OutputFileArgTemplate = "--skip-git-repo-check --json --output-last-message {path}",
                ReadOutputFile = true,
                DeleteOutputFileAfterRead = true,
                SupportsStreaming = false,
                SupportsResume = true,
                ResumeArgTemplate = "resume {sessionId}",
                ResumeSessionIdRegex = "\"thread_id\"\\s*:\\s*\"(?<sessionId>[^\"]+)\"",
                Timeout = DefaultTimeoutSeconds,
                MaxOutputBytes = DefaultMaxOutputBytes,
                TrustLevel = "prompt",
                PermissionModeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SubAgentApprovalModeResolver.InteractiveMode] = "--sandbox read-only",
                    [SubAgentApprovalModeResolver.AutoApproveMode] = "--dangerously-bypass-approvals-and-sandbox",
                    [SubAgentApprovalModeResolver.RestrictedMode] = "--sandbox read-only"
                }
            },
            new SubAgentProfile
            {
                Name = "cursor-cli",
                Runtime = CliOneshotRuntime.RuntimeTypeName,
                Bin = "cursor-agent",
                Args =
                [
                    "-p",
                    "--output-format",
                    "json"
                ],
                WorkingDirectoryMode = "workspace",
                InputMode = "arg",
                OutputFormat = "json",
                OutputJsonPath = "result",
                SupportsStreaming = false,
                SupportsResume = true,
                ResumeArgTemplate = "--resume {sessionId}",
                ResumeSessionIdJsonPath = "session_id",
                Timeout = DefaultTimeoutSeconds,
                MaxOutputBytes = DefaultMaxOutputBytes,
                TrustLevel = "prompt",
                PermissionModeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SubAgentApprovalModeResolver.InteractiveMode] = "--mode ask --trust --approve-mcps",
                    [SubAgentApprovalModeResolver.AutoApproveMode] = "--mode auto --trust --approve-mcps",
                    [SubAgentApprovalModeResolver.RestrictedMode] = "--mode ask"
                }
            },
            new SubAgentProfile
            {
                Name = "custom-cli-oneshot",
                Runtime = CliOneshotRuntime.RuntimeTypeName,
                WorkingDirectoryMode = "workspace",
                InputMode = "arg",
                OutputFormat = "text",
                SupportsStreaming = false,
                SupportsResume = false,
                Timeout = 120,
                MaxOutputBytes = DefaultMaxOutputBytes,
                TrustLevel = "restricted"
            }
        ];

    public IReadOnlyList<string> ValidationWarnings => _validationWarnings;

    public IReadOnlyCollection<SubAgentProfile> Profiles => _profiles.Values.ToArray();

    public bool IsBuiltInProfile(string name) => _builtInProfileNames.Contains(name);

    public bool IsTemplateProfile(string name) => _builtInTemplateProfileNames.Contains(name);

    public bool IsEnabled(string name)
    {
        if (string.Equals(name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            return true;

        return !_disabledProfileNames.Contains(name);
    }

    public IReadOnlyList<string> GetHiddenBuiltInReasons(Func<string, bool>? binaryAvailabilityProbe = null)
    {
        binaryAvailabilityProbe ??= bin => CliOneshotRuntime.TryResolveExecutablePath(bin, out _);
        var reasons = new List<string>();
        foreach (var profile in _profiles.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!_builtInProfileNames.Contains(profile.Name))
                continue;

            if (!IsEnabled(profile.Name))
                continue;

            if (!string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(profile.Bin))
                continue;

            if (binaryAvailabilityProbe(profile.Bin))
                continue;

            reasons.Add(
                $"SubAgent profile '{profile.Name}' is hidden from the agent because '{profile.Bin}' was not found on PATH.");
        }

        return reasons;
    }

    public IReadOnlyList<string> GetValidationWarningsForProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        return _validationWarnings
            .Where(w => w.Contains($"'{name}'", StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyList<string> ValidateProfiles(
        IEnumerable<SubAgentProfile>? profiles,
        IEnumerable<string>? knownRuntimeTypes = null,
        IEnumerable<string>? templateProfileNames = null)
    {
        var warnings = new List<string>();
        if (profiles == null)
            return warnings;

        var runtimeSet = knownRuntimeTypes != null
            ? new HashSet<string>(knownRuntimeTypes, StringComparer.OrdinalIgnoreCase)
            : null;
        var templateSet = templateProfileNames != null
            ? new HashSet<string>(templateProfileNames, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                continue;

            var isTemplateProfile = templateSet?.Contains(profile.Name) == true;

            if (string.IsNullOrWhiteSpace(profile.Runtime))
            {
                warnings.Add($"SubAgent profile '{profile.Name}' is missing a runtime.");
                continue;
            }

            if (runtimeSet != null && !runtimeSet.Contains(profile.Runtime))
            {
                warnings.Add(
                    $"SubAgent profile '{profile.Name}' references unknown runtime '{profile.Runtime}'.");
            }

            if (!string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!isTemplateProfile && string.IsNullOrWhiteSpace(profile.Bin))
                warnings.Add($"SubAgent profile '{profile.Name}' is missing required field 'bin' for cli-oneshot.");

            if (!isTemplateProfile
                && string.Equals(profile.InputMode, "arg-template", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(profile.InputArgTemplate))
            {
                warnings.Add(
                    $"SubAgent profile '{profile.Name}' uses inputMode 'arg-template' but does not define inputArgTemplate.");
            }

            if (string.Equals(profile.InputMode, "env", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(profile.InputEnvKey))
            {
                warnings.Add(
                    $"SubAgent profile '{profile.Name}' uses inputMode 'env' but does not define inputEnvKey.");
            }

            if (!isTemplateProfile
                && string.Equals(profile.OutputFormat, "json", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(profile.OutputJsonPath))
            {
                warnings.Add(
                    $"SubAgent profile '{profile.Name}' uses outputFormat 'json' but does not define outputJsonPath.");
            }

            if (!isTemplateProfile
                && profile.ReadOutputFile == true
                && string.IsNullOrWhiteSpace(profile.OutputFileArgTemplate))
            {
                warnings.Add(
                    $"SubAgent profile '{profile.Name}' enables output file capture but does not define outputFileArgTemplate.");
            }

            if (profile.SupportsResume == true)
            {
                if (string.IsNullOrWhiteSpace(profile.ResumeArgTemplate))
                {
                    warnings.Add(
                        $"SubAgent profile '{profile.Name}' enables resume but does not define resumeArgTemplate.");
                }

                if (string.IsNullOrWhiteSpace(profile.ResumeSessionIdJsonPath)
                    && string.IsNullOrWhiteSpace(profile.ResumeSessionIdRegex))
                {
                    warnings.Add(
                        $"SubAgent profile '{profile.Name}' enables resume but does not define resumeSessionIdJsonPath or resumeSessionIdRegex.");
                }
            }
        }

        return warnings;
    }

    public bool TryGet(string name, out SubAgentProfile profile)
    {
        if (_profiles.TryGetValue(name, out var existing))
        {
            profile = existing.Clone();
            return true;
        }

        profile = new SubAgentProfile();
        return false;
    }
}

public sealed class SubAgentCoordinator
{
    public const string DefaultProfileName = "native";

    private readonly string _workspaceRoot;
    private readonly SubAgentProfileRegistry _profileRegistry;
    private readonly IReadOnlyDictionary<string, ISubAgentRuntime> _runtimes;
    private readonly IApprovalService? _approvalService;
    private readonly IExternalCliSessionStore? _externalCliSessionStore;
    private readonly bool _enableExternalCliSessionResume;

    public SubAgentCoordinator(
        string workspaceRoot,
        IEnumerable<ISubAgentRuntime> runtimes,
        IEnumerable<SubAgentProfile>? configuredProfiles = null,
        IApprovalService? approvalService = null,
        IEnumerable<string>? disabledProfiles = null,
        IExternalCliSessionStore? externalCliSessionStore = null,
        bool enableExternalCliSessionResume = false)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _approvalService = approvalService;
        _externalCliSessionStore = externalCliSessionStore;
        _enableExternalCliSessionResume = enableExternalCliSessionResume;
        var runtimeMap = new Dictionary<string, ISubAgentRuntime>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtime in runtimes)
            runtimeMap[runtime.RuntimeType] = runtime;
        _runtimes = runtimeMap;

        _profileRegistry = new SubAgentProfileRegistry(
            configuredProfiles,
            SubAgentProfileRegistry.CreateBuiltInProfiles(),
            _runtimes.Keys,
            disabledProfiles);
    }

    public IReadOnlyList<string> ValidationWarnings => _profileRegistry.ValidationWarnings;

    public bool ExternalCliSessionResumeEnabled => _enableExternalCliSessionResume;

    public IReadOnlyList<SubAgentProfileDiagnostic> GetProfileDiagnostics()
    {
        var diagnostics = new List<SubAgentProfileDiagnostic>();
        foreach (var profile in _profileRegistry.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var warnings = _profileRegistry.GetValidationWarningsForProfile(profile.Name);
            var runtimeRegistered = _runtimes.ContainsKey(profile.Runtime);
            var enabled = _profileRegistry.IsEnabled(profile.Name);

            string? resolvedBinary = null;
            var hiddenReasons = new List<string>();
            if (!enabled)
            {
                hiddenReasons.Add("disabled by workspace configuration");
            }

            if (!runtimeRegistered)
            {
                hiddenReasons.Add("runtime not registered");
            }

            if (_profileRegistry.IsTemplateProfile(profile.Name))
            {
                hiddenReasons.Add("template profile");
            }

            if (warnings.Count > 0)
            {
                hiddenReasons.Add("configuration warnings present");
            }

            if (string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(profile.Bin))
                {
                    hiddenReasons.Add("missing required field 'bin'");
                }
                else if (runtimeRegistered)
                {
                    if (CliOneshotRuntime.TryResolveExecutablePath(profile.Bin, out var resolved))
                    {
                        resolvedBinary = resolved;
                    }
                    else
                    {
                        hiddenReasons.Add($"binary '{profile.Bin}' was not found on PATH");
                    }
                }
            }

            var hiddenFromPrompt = hiddenReasons.Count > 0;
            var hiddenReason = hiddenFromPrompt
                ? string.Join("; ", hiddenReasons.Distinct(StringComparer.Ordinal))
                : null;

            diagnostics.Add(new SubAgentProfileDiagnostic
            {
                Name = profile.Name,
                Runtime = profile.Runtime,
                WorkingDirectoryMode = profile.WorkingDirectoryMode,
                IsBuiltIn = _profileRegistry.IsBuiltInProfile(profile.Name),
                Enabled = enabled,
                Bin = profile.Bin,
                ResolvedBinary = resolvedBinary,
                BinaryResolved = resolvedBinary != null,
                RuntimeRegistered = runtimeRegistered,
                HiddenFromPrompt = hiddenFromPrompt,
                HiddenReason = hiddenReason,
                Warnings = warnings
            });
        }

        return diagnostics;
    }

    public async Task<string> RunAsync(
        SubAgentTaskRequest request,
        string? profileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prepared = PrepareRun(request, profileName);
            var result = await ExecutePreparedRunAsync(prepared, cancellationToken: cancellationToken);
            return result.Text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public SubAgentPreparedRun PrepareRun(
        SubAgentTaskRequest request,
        string? profileName = null)
    {
        var effectiveProfileName = string.IsNullOrWhiteSpace(profileName)
            ? DefaultProfileName
            : profileName.Trim();

        if (!_profileRegistry.TryGet(effectiveProfileName, out var profile))
            throw new InvalidOperationException($"Unknown subagent profile '{effectiveProfileName}'.");

        if (!_profileRegistry.IsEnabled(profile.Name))
            throw new InvalidOperationException($"Subagent profile '{profile.Name}' is disabled.");

        if (!_runtimes.TryGetValue(profile.Runtime, out var runtime))
            throw new InvalidOperationException(
                $"Subagent profile '{profile.Name}' references unknown runtime '{profile.Runtime}'.");

        var workingDirectory = ResolveWorkingDirectory(profile, request);
        var approvalContext = request.ApprovalContext ?? ApprovalContextScope.Current;

        string? resumeSessionId = null;
        if (_enableExternalCliSessionResume
            && profile.SupportsResume == true
            && _externalCliSessionStore?.TryGetResumeSession(profile.Name, request.Label, workingDirectory, out var storedSession) == true)
        {
            resumeSessionId = storedSession.SessionId;
        }

        var launchContext = new SubAgentLaunchContext(
            WorkspaceRoot: _workspaceRoot,
            WorkingDirectory: workingDirectory,
            ProfileName: profile.Name,
            ExtraLaunchArgs: ResolvePermissionModeArgs(profile, approvalContext),
            ResumeSessionId: resumeSessionId,
            ApprovalMode: SubAgentApprovalModeResolver.Resolve(_approvalService, approvalContext),
            ApprovalService: _approvalService,
            ApprovalContext: approvalContext);

        var effectiveRequest = request with
        {
            WorkingDirectory = workingDirectory,
            ApprovalContext = approvalContext
        };

        return new SubAgentPreparedRun(profile, runtime, launchContext, effectiveRequest);
    }

    public async Task<SubAgentRunResult> ExecutePreparedRunAsync(
        SubAgentPreparedRun prepared,
        ISubAgentEventSink? sink = null,
        CancellationToken cancellationToken = default)
    {
        sink ??= CreateEventSink(prepared.Request, prepared.Runtime.RuntimeType);
        var profile = prepared.Profile;
        var runtime = prepared.Runtime;
        var session = await runtime.CreateSessionAsync(profile, prepared.LaunchContext, cancellationToken);
        try
        {
            var result = await runtime.RunAsync(session, prepared.Request, sink, cancellationToken);
            if (!result.IsError
                && _enableExternalCliSessionResume
                && profile.SupportsResume == true
                && !string.IsNullOrWhiteSpace(result.SessionId))
            {
                _externalCliSessionStore?.RecordSuccessfulRun(
                    profile.Name,
                    prepared.Request.Label,
                    prepared.LaunchContext.WorkingDirectory,
                    result.SessionId);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            await runtime.CancelAsync(session, CancellationToken.None);
            throw;
        }
        finally
        {
            await runtime.DisposeSessionAsync(session, CancellationToken.None);
        }
    }

    private string ResolveWorkingDirectory(SubAgentProfile profile, SubAgentTaskRequest request)
    {
        var mode = profile.WorkingDirectoryMode?.Trim();
        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "workspace", StringComparison.OrdinalIgnoreCase))
            return _workspaceRoot;

        if (string.Equals(mode, "specified", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
                throw new InvalidOperationException(
                    $"Subagent profile '{profile.Name}' requires a specified working directory.");

            var specifiedDirectory = Path.GetFullPath(request.WorkingDirectory);
            if (!Directory.Exists(specifiedDirectory))
            {
                throw new InvalidOperationException(
                    $"Subagent working directory '{specifiedDirectory}' does not exist.");
            }

            return specifiedDirectory;
        }

        throw new InvalidOperationException(
            $"Subagent profile '{profile.Name}' has unsupported workingDirectoryMode '{profile.WorkingDirectoryMode}'.");
    }

    private static ISubAgentEventSink CreateEventSink(SubAgentTaskRequest request, string runtimeType)
    {
        if (string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            return NullSubAgentEventSink.Instance;

        var bridgeKey = SubAgentManager.NormalizeLabel(request.Label, request.Task);
        var progressEntry = SubAgentProgressBridge.GetOrCreate(bridgeKey);
        return new BridgeSubAgentEventSink(progressEntry, bridgeKey);
    }

    private IReadOnlyList<string> ResolvePermissionModeArgs(SubAgentProfile profile, ApprovalContext? approvalContext)
    {
        if (!string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            return [];

        if (profile.PermissionModeMapping == null || profile.PermissionModeMapping.Count == 0)
            return [];

        var mode = SubAgentApprovalModeResolver.Resolve(_approvalService, approvalContext);
        var mappedValue = profile.PermissionModeMapping
            .FirstOrDefault(kvp => string.Equals(kvp.Key, mode, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (string.IsNullOrWhiteSpace(mappedValue))
            return [];

        return CliOneshotRuntime.SplitArguments(mappedValue);
    }

    private sealed class BridgeSubAgentEventSink(
        SubAgentProgressBridge.ProgressEntry progressEntry,
        string label) : ISubAgentEventSink
    {
        public void OnInfo(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            OnProgress("external-cli", message.Trim());
        }

        public void OnProgress(string? currentTool, string? currentToolDisplay = null)
        {
            progressEntry.CurrentTool = string.IsNullOrWhiteSpace(currentTool) ? "external-cli" : currentTool;
            progressEntry.LastTool = progressEntry.CurrentTool;
            if (!string.IsNullOrWhiteSpace(currentToolDisplay))
            {
                progressEntry.CurrentToolDisplay = currentToolDisplay.Trim();
                progressEntry.LastToolDisplay = progressEntry.CurrentToolDisplay;
            }
        }

        public void OnCompleted(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
        {
            ApplyTokens(tokensUsed);
            progressEntry.CurrentTool = null;
            progressEntry.CurrentToolDisplay = null;
            progressEntry.LastTool = "completed";
            progressEntry.LastToolDisplay = string.IsNullOrWhiteSpace(summary)
                ? $"Completed {label}"
                : summary.Trim();
            progressEntry.IsCompleted = true;
        }

        public void OnFailed(string? summary = null, SubAgentTokenUsage? tokensUsed = null)
        {
            ApplyTokens(tokensUsed);
            progressEntry.CurrentTool = null;
            progressEntry.CurrentToolDisplay = null;
            progressEntry.LastTool = "failed";
            progressEntry.LastToolDisplay = string.IsNullOrWhiteSpace(summary)
                ? $"Failed {label}"
                : summary.Trim();
            progressEntry.IsCompleted = true;
        }

        private void ApplyTokens(SubAgentTokenUsage? tokensUsed)
        {
            if (tokensUsed == null)
                return;

            progressEntry.AddTokens(
                tokensUsed.InputTokens,
                tokensUsed.OutputTokens,
                tokensUsed.CachedInputTokens,
                tokensUsed.CacheWriteInputTokens,
                tokensUsed.ReasoningOutputTokens,
                tokensUsed.InputTokens > 0 || tokensUsed.OutputTokens > 0 ? 1 : 0);
            TokenTracker.Current?.AddSubAgentTokens(
                tokensUsed.InputTokens,
                tokensUsed.OutputTokens,
                tokensUsed.CachedInputTokens,
                tokensUsed.CacheWriteInputTokens,
                tokensUsed.ReasoningOutputTokens,
                tokensUsed.InputTokens > 0 || tokensUsed.OutputTokens > 0 ? 1 : 0);
        }
    }
}
