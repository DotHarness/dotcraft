using System.Text;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.TraceViewer.Analysis;

internal interface ITraceAnalystService : IAsyncDisposable
{
    Task<TraceReview> AnalyzeAsync(
        TraceSnapshot snapshot,
        string dataPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<string> AskAsync(
        TraceSnapshot snapshot,
        string dataPath,
        TraceReview review,
        string question,
        string? attachment,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    void CommitEvidence(TraceSnapshot snapshot);

    void Cancel();
}

internal sealed class TraceAnalystService : ITraceAnalystService
{
    private static readonly string[] ToolNames = ["ReadFile", "FindFiles", "GrepFiles", "SubmitTraceReview"];
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly TraceAnalysisContext _context = new();
    private readonly string _analysisRoot;
    private readonly TraceEvidenceBundleStore _evidenceBundles;
    private readonly Action<IServiceCollection>? _configureServices;
    private IHost? _host;
    private string? _configuredWorkspace;
    private string? _configuredModel;
    private CancellationTokenSource? _activeTurn;

    public TraceAnalystService(
        string? analysisRoot = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _analysisRoot = Path.GetFullPath(analysisRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotCraft",
            "TraceViewer",
            "analysis"));
        _evidenceBundles = new TraceEvidenceBundleStore(_analysisRoot);
        _configureServices = configureServices;
    }

    public bool IsBusy => _turnGate.CurrentCount == 0;

    public async Task<TraceReview> AnalyzeAsync(
        TraceSnapshot snapshot,
        string dataPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await _turnGate.WaitAsync(cancellationToken);
        TraceEvidenceBundle? bundle = null;
        var completed = false;
        try
        {
            _activeTurn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _context.Progress = progress;
            progress?.Report("Preparing trace evidence…");
            bundle = await Task.Run(
                () => _evidenceBundles.Prepare(snapshot, _activeTurn.Token),
                _activeTurn.Token);
            progress?.Report("Starting DotCraft analyst…");
            await EnsureHostAsync(snapshot.WorkspacePath, dataPath, _activeTurn.Token);
            var sessions = _host!.Services.GetRequiredService<ISessionService>();
            var thread = await sessions.CreateThreadAsync(
                CreateIdentity(),
                CreateThreadConfiguration(bundle.Path),
                displayName: $"Trace review: {snapshot.SessionKey}",
                ct: _activeTurn.Token);
            _context.Snapshot = snapshot;
            _context.AnalystThreadId = thread.Id;
            _context.ModelId = _configuredModel;
            _context.SubmittedReview = null;
            progress?.Report("Scanning trace evidence…");
            await RunTurnAsync(sessions, thread.Id, InitialPrompt(snapshot), progress, _activeTurn.Token);
            var review = _context.SubmittedReview
                ?? throw new InvalidDataException("The analyst completed without submitting a structured review.");
            completed = true;
            return review;
        }
        finally
        {
            if (!completed && bundle is { Created: true })
                _evidenceBundles.Delete(snapshot);
            ResetActiveContext();
            _turnGate.Release();
        }
    }

    public async Task<string> AskAsync(
        TraceSnapshot snapshot,
        string dataPath,
        TraceReview review,
        string question,
        string? attachment,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await _turnGate.WaitAsync(cancellationToken);
        try
        {
            _activeTurn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _context.Progress = progress;
            progress?.Report("Preparing trace evidence…");
            await Task.Run(
                () => _evidenceBundles.Ensure(snapshot, _activeTurn.Token),
                _activeTurn.Token);
            progress?.Report("Starting DotCraft analyst…");
            await EnsureHostAsync(snapshot.WorkspacePath, dataPath, _activeTurn.Token);
            var sessions = _host!.Services.GetRequiredService<ISessionService>();
            await sessions.ResumeThreadAsync(review.AnalystThreadId, _activeTurn.Token);
            _context.Snapshot = snapshot;
            _context.AnalystThreadId = review.AnalystThreadId;
            _context.ModelId = _configuredModel;
            var prompt = string.IsNullOrWhiteSpace(attachment)
                ? question
                : $"Attached review evidence:\n{attachment}\n\nUser question:\n{question}";
            return await RunTurnAsync(sessions, review.AnalystThreadId, prompt, progress, _activeTurn.Token);
        }
        finally
        {
            ResetActiveContext();
            _turnGate.Release();
        }
    }

    public void CommitEvidence(TraceSnapshot snapshot) => _evidenceBundles.KeepOnly(snapshot);

    public void Cancel() => _activeTurn?.Cancel();

    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }
        _turnGate.Dispose();
    }

    private async Task EnsureHostAsync(string targetWorkspace, string dataPath, CancellationToken ct)
    {
        var normalized = Path.GetFullPath(targetWorkspace);
        if (_host is not null && string.Equals(_configuredWorkspace, normalized, StringComparison.OrdinalIgnoreCase))
            return;
        if (_host is not null)
        {
            await _host.StopAsync(ct);
            _host.Dispose();
        }

        Directory.CreateDirectory(_analysisRoot);
        var userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft");
        var config = AppConfig.LoadWithGlobalFallback(
            Path.Combine(dataPath, "config.json"),
            Path.Combine(userDataPath, "config.json"));
        config.Tracing.Enabled = false;
        config.McpServers = [];
        config.Plugins.EnabledPlugins = [];
        config.Hooks.Enabled = false;
        config.Security.BlacklistedPaths = config.Security.BlacklistedPaths
            .Append(userDataPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _configuredModel = ModelProviderResolver.ResolveMain(config).Model;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(_context);
        builder.Services.AddSingleton<IToolSource, TraceReviewSubmissionToolSource>();
        builder.Services.AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = _analysisRoot;
            options.DataPath = ".agents";
            options.UserDataPath = Directory.Exists(userDataPath) ? userDataPath : null;
        });
        _configureServices?.Invoke(builder.Services);
        _host = builder.Build();
        await _host.StartAsync(ct);
        _host.Services.GetRequiredService<SkillsLoader>()
            .DeployBuiltInSkills(typeof(TraceAnalystService).Assembly);
        _configuredWorkspace = normalized;
    }

    private static ThreadConfiguration CreateThreadConfiguration(string bundlePath) => new()
    {
        Mode = "agent",
        Cwd = bundlePath,
        RuntimeWorkspaceRoots = [bundlePath],
        ToolAllowList = ToolNames,
        ToolPolicy = new ThreadToolPolicy { Allow = ToolNames },
        McpServers = [],
        McpPolicy = new ThreadMcpPolicy { Servers = [], Tools = new ThreadNamePolicy { Allow = [] } },
        PluginPolicy = new ThreadPluginPolicy { Allow = [] },
        SkillsPolicy = new ThreadSkillsPolicy
        {
            Allow = ["trace-review"],
            Preload = ["trace-review"],
            AllowManage = false
        },
        AgentControlToolAccess = AgentControlToolAccess.Disabled,
        ApprovalPolicy = ApprovalPolicy.AutoApprove,
        RequireApprovalOutsideWorkspace = false,
        RoleInstructions = AnalystInstructions,
        OverrideBasePrompt = false
    };

    private SessionIdentity CreateIdentity() => new()
    {
        ChannelName = "trace-viewer",
        UserId = "trace-analyst",
        WorkspacePath = _analysisRoot,
        ChannelContext = "trace-review"
    };

    private static async Task<string> RunTurnAsync(
        ISessionService sessions,
        string threadId,
        string prompt,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var response = new StringBuilder();
        string? failure = null;
        await foreach (var item in sessions.SubmitInputAsync(threadId, prompt, ct: ct))
        {
            if (item.DeltaPayload?.TextDelta is { } text)
                response.Append(text);
            if (item.EventType == SessionEventType.ItemStarted
                && item.ItemPayload?.AsToolCall?.ToolName is { } toolName)
            {
                progress?.Report(toolName switch
                {
                    "FindFiles" or "GrepFiles" => "Scanning trace evidence…",
                    "ReadFile" => "Inspecting trace evidence…",
                    "SubmitTraceReview" => "Validating review findings…",
                    _ => "Reviewing trace evidence…"
                });
            }
            if (item.EventType == SessionEventType.TurnFailed)
                failure = item.TurnFailedPayload?.Error ?? "Analysis failed.";
        }
        if (failure is not null)
            throw new InvalidOperationException(failure);
        return response.ToString().Trim();
    }

    private static string InitialPrompt(TraceSnapshot snapshot) => $"""
        Review immutable Trace session {snapshot.SessionKey} at revision {snapshot.Revision}.
        The current workspace is its Evidence Bundle. Follow the preloaded trace-review skill and finish by calling SubmitTraceReview.
        """;

    private void ResetActiveContext()
    {
        _context.Snapshot = null;
        _context.AnalystThreadId = null;
        _context.ModelId = null;
        _context.Progress = null;
        _activeTurn?.Dispose();
        _activeTurn = null;
    }

    private const string AnalystInstructions = """
        You are the DotCraft Trace Analyst. Review only the immutable Evidence Bundle in the current workspace.
        Keep conclusions concise and evidence-linked. Complete an initial review only through SubmitTraceReview.
        For follow-up answers, cite verified evidence as trace://event/{eventId} and never invent an Event id.
        """;
}
