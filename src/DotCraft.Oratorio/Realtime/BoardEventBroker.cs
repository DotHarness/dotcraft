using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Realtime;

/// <summary>
/// Routes board-wide updates and focused drawer events to browser stream subscribers.
/// </summary>
public class BoardEventHub
{
    private readonly ConcurrentDictionary<Guid, BoardEventSubscriber> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public BoardEventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var subscriber = new BoardEventSubscriber(
            CreateChannel<BoardEvent>(),
            CreateChannel<DrawerEvent>(),
            CreateChannel<SourceSyncEvent>());
        _subscribers[id] = subscriber;
        return new BoardEventSubscription(id, subscriber, () => Remove(id));
    }

    public void PublishBoard(BoardEvent boardEvent)
    {
        foreach (var (_, subscriber) in _subscribers)
        {
            subscriber.Board.Writer.TryWrite(boardEvent);
        }
    }

    public void PublishDrawer(string runId, DrawerEvent drawerEvent)
    {
        foreach (var (_, subscriber) in _subscribers)
        {
            if (subscriber.IsFocused(runId))
            {
                subscriber.Drawer.Writer.TryWrite(drawerEvent);
            }
        }
    }

    public void PublishSourceSync(SourceSyncEvent sourceEvent)
    {
        foreach (var (_, subscriber) in _subscribers)
        {
            subscriber.SourceSync.Writer.TryWrite(sourceEvent);
        }
    }

    public void PublishTaskUpdated(OratorioItem item, DateTimeOffset timestamp) =>
        PublishBoard(BoardEvent.TaskUpdated(item, timestamp));

    public void Publish(BoardEvent boardEvent) =>
        PublishBoard(boardEvent);

    private static Channel<T> CreateChannel<T>() =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private void Remove(Guid id)
    {
        if (_subscribers.TryRemove(id, out var subscriber))
        {
            subscriber.Complete();
        }
    }
}

/// <summary>
/// Represents one active browser stream subscription.
/// </summary>
public sealed class BoardEventSubscription(Guid id, BoardEventSubscriber subscriber, Action dispose) : IDisposable
{
    private int _disposed;

    public Guid Id { get; } = id;

    public ChannelReader<BoardEvent> Reader => BoardReader;

    public ChannelReader<BoardEvent> BoardReader { get; } = subscriber.Board.Reader;

    public ChannelReader<DrawerEvent> DrawerReader { get; } = subscriber.Drawer.Reader;

    public ChannelReader<SourceSyncEvent> SourceSyncReader { get; } = subscriber.SourceSync.Reader;

    public void Focus(string runId) =>
        subscriber.Focus(runId);

    public void Unfocus(string runId) =>
        subscriber.Unfocus(runId);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            dispose();
        }
    }
}

public sealed class BoardEventSubscriber(Channel<BoardEvent> board, Channel<DrawerEvent> drawer, Channel<SourceSyncEvent> sourceSync)
{
    private readonly object _focusGate = new();
    private readonly HashSet<string> _focusedRunIds = new(StringComparer.Ordinal);

    public Channel<BoardEvent> Board { get; } = board;

    public Channel<DrawerEvent> Drawer { get; } = drawer;

    public Channel<SourceSyncEvent> SourceSync { get; } = sourceSync;

    public void Focus(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        lock (_focusGate)
        {
            _focusedRunIds.Add(runId);
        }
    }

    public void Unfocus(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        lock (_focusGate)
        {
            _focusedRunIds.Remove(runId);
        }
    }

    public bool IsFocused(string runId)
    {
        lock (_focusGate)
        {
            return _focusedRunIds.Contains(runId);
        }
    }

    public void Complete()
    {
        Board.Writer.TryComplete();
        Drawer.Writer.TryComplete();
        SourceSync.Writer.TryComplete();
    }
}

public sealed record BoardEvent(
    string Type,
    string? TaskId,
    string? ShortId,
    BoardTaskStatus? TaskStatus,
    string? MicroStatus,
    long? BoardSortOrder,
    DateTimeOffset Ts)
{
    public const string TaskUpdatedType = "task/updated";
    public const string TaskRemovedType = "task/removed";
    public const string PingType = "ping";

    public static BoardEvent TaskUpdated(OratorioItem item, DateTimeOffset timestamp) =>
        new(
            TaskUpdatedType,
            item.ItemId,
            item.ShortId,
            TaskStatusMapping.Project(item.State),
            MicroStatusFromItem(item),
            item.BoardSortOrder,
            timestamp);

    public static BoardEvent Ping(DateTimeOffset timestamp) =>
        new(PingType, null, null, null, null, null, timestamp);

    private static string MicroStatusFromItem(OratorioItem item) =>
        item.State switch
        {
            ItemState.Dispatching or ItemState.Running => "running",
            ItemState.Failed => "error",
            _ when item.CheckState == CheckState.Attention => "awaiting-approval",
            _ => "idle"
        };
}

public sealed record DrawerEvent(
    string Type,
    string RunId,
    string? TaskId,
    string? ShortId,
    JsonElement Payload,
    DateTimeOffset Ts)
{
    public const string ItemStartedType = "drawer/item.started";
    public const string ItemDeltaType = "drawer/item.delta";
    public const string ItemCompletedType = "drawer/item.completed";
    public const string PlanUpdatedType = "drawer/plan/updated";
    public const string RunStatusType = "drawer/run/status";
}

public sealed record SourceSyncEvent(
    string Type,
    JsonElement Payload,
    DateTimeOffset Ts)
{
    public const string GitHubSyncJobUpdatedType = "source/github-sync/job.updated";
    public const string GitHubSyncRepositoryUpdatedType = "source/github-sync/repository.updated";
    public const string SourceSyncJobUpdatedType = "source/sync/job.updated";
    public const string SourceSyncProjectUpdatedType = "source/sync/project.updated";
    public const string SourceSyncScheduleUpdatedType = "source/sync/schedule.updated";
}
