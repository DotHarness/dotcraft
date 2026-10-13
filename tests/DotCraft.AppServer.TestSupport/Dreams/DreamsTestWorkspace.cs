using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsTestWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Dreams_" + Guid.NewGuid().ToString("N")[..8]);

    public DreamsTestWorkspace()
    {
        WorkspacePath = Path.Combine(_root, "workspace");
        CraftPath = Path.Combine(WorkspacePath, ".craft");
        Directory.CreateDirectory(CraftPath);
        Threads = new ThreadStore(CraftPath);
        Config = new AppConfig
        {
            Dreams = new DreamsConfig { Enabled = true, StartupDelay = TimeSpan.Zero }
        };
    }

    public string WorkspacePath { get; }

    public string CraftPath { get; }

    public ThreadStore Threads { get; }

    public AppConfig Config { get; }

    public DotCraftPaths Paths => new(WorkspacePath, CraftPath, userDataPath: null);

    public string WorkspaceStore => MemoryStore.DirectoryOf(CraftPath);

    public DreamsArtifacts Artifacts => new(CraftPath);

    public DreamsStateStore States => new(Artifacts.DreamsDirectoryPath);

    public string StoreOf(string scope) =>
        MemoryScopes.StoreDirectory(new ThreadConfiguration { MemoryScope = scope }, CraftPath);

    public DreamsService CreateService(IDreamsRunner runner, IAppConfigMonitor? monitor = null) =>
        new(Config, Paths, Threads, runner, monitor);

    public static string WriteFile(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteMemory(string name, string fact, string? store = null, string type = "project")
    {
        var directory = store ?? WorkspaceStore;
        var path = WriteFile(
            directory,
            $"{name}.md",
            $"---\nname: {name}\ndescription: {fact}\nmetadata:\n  type: {type}\n---\n\n{fact}\n");
        File.AppendAllText(Path.Combine(directory, MemoryStore.IndexFileName), $"- [{name}]({name}.md) — {fact}\n");
        return path;
    }

    public static void SetLastConsolidated(string store, DateTimeOffset at)
    {
        var path = WriteFile(store, ConsolidationLock.FileName, string.Empty);
        File.SetLastWriteTimeUtc(path, at.UtcDateTime);
    }

    public async Task SaveThreadAsync(
        string id,
        DateTimeOffset? completedAt = null,
        string? scope = null,
        string userText = "Please remember the deployment checklist.",
        Action<SessionThread>? configure = null)
    {
        var completed = completedAt ?? DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = $"{id}_user",
            TurnId = $"{id}_turn",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = userText },
            CreatedAt = completed.AddSeconds(-30)
        };
        var agent = new SessionItem
        {
            Id = $"{id}_agent",
            TurnId = $"{id}_turn",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "Noted." },
            CreatedAt = completed
        };
        var thread = new SessionThread
        {
            Id = id,
            WorkspacePath = WorkspacePath,
            OriginChannel = "desktop",
            DisplayName = $"Thread {id}",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = completed.AddMinutes(-5),
            LastActiveAt = completed,
            HistoryMode = HistoryMode.Server,
            Configuration = new ThreadConfiguration { MemoryScope = scope, MemoryEnabled = true },
            Turns =
            [
                new SessionTurn
                {
                    Id = $"{id}_turn",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    Input = user,
                    Items = [user, agent],
                    StartedAt = completed.AddMinutes(-1),
                    CompletedAt = completed
                }
            ]
        };
        configure?.Invoke(thread);
        await Threads.SaveThreadAsync(thread);
    }

    public async Task<DreamsRunState> WaitForRunAsync(Func<DreamsRunState, bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (States.List(includeArchived: true).FirstOrDefault(predicate) is { } run)
                return run;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("No Dream run reached the expected state.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

/// <summary>A Dreams runner that records its input and edits the candidate store instead of calling a model.</summary>
public sealed class FakeDreamsRunner : IDreamsRunner
{
    private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<DreamsRunInput> _inputs = [];

    public string ModelId => "fake-dream-model";

    public IReadOnlyList<DreamsRunInput> Inputs
    {
        get
        {
            lock (_inputs)
                return _inputs.ToArray();
        }
    }

    public bool BlockUntilReleased { get; init; }

    public bool Canceled { get; private set; }

    public Action<DreamsRunInput>? Edit { get; set; }

    public Func<DreamsRunInput, DreamsGenerationResult>? Result { get; set; }

    public async Task<DreamsGenerationResult> GenerateAsync(
        DreamsRunInput input,
        string modelId,
        Action<DreamsRunSessionBinding>? onSessionBinding = null,
        CancellationToken cancellationToken = default)
    {
        lock (_inputs)
            _inputs.Add(input);
        onSessionBinding?.Invoke(new DreamsRunSessionBinding("thread_dream_fake", "turn_dream_fake"));
        _called.TrySetResult();
        if (BlockUntilReleased)
        {
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled = true;
                throw;
            }
        }

        Edit?.Invoke(input);
        return Result?.Invoke(input) ?? new DreamsGenerationResult(
            true,
            "Consolidated the store.",
            ThreadId: "thread_dream_fake",
            TurnId: "turn_dream_fake",
            Diagnostics: new DreamsRunDiagnostics(["thread_one"], 2, 1));
    }

    public Task WaitUntilCalledAsync() => _called.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Release() => _release.TrySetResult();
}
