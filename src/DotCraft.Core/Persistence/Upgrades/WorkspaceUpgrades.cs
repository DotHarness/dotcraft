using DotCraft.Sessions;
using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence.Upgrades;

internal interface IWorkspaceUpgrade
{
    string Version { get; }

    void Apply(SqliteConnection connection);

    Task ApplyInBackgroundAsync(ThreadStore threadStore, CancellationToken ct);
}

/// <summary>
/// Brings an existing workspace's data to the current release once, recording the release in
/// <c>workspace_meta</c>. Only the current release's upgrade is kept here; the next release removes it.
/// </summary>
internal static class WorkspaceUpgrades
{
    private const string DataVersionKey = "data_version";

    private static readonly IWorkspaceUpgrade? Current = new WorkspaceUpgrade_0_9_3();

    public static void Apply(SqliteConnection connection)
    {
        if (Current == null || string.Equals(ReadDataVersion(connection), Current.Version, StringComparison.Ordinal))
            return;

        Current.Apply(connection);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspace_meta (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", DataVersionKey);
        command.Parameters.AddWithValue("$value", Current.Version);
        command.ExecuteNonQuery();
    }

    public static Task ApplyInBackgroundAsync(ThreadStore threadStore, CancellationToken ct) =>
        Current?.ApplyInBackgroundAsync(threadStore, ct) ?? Task.CompletedTask;

    public static IAsyncDisposable StartInBackground(ThreadStore threadStore) => new BackgroundUpgrade(threadStore);

    private static string? ReadDataVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM workspace_meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", DataVersionKey);
        return command.ExecuteScalar() as string;
    }

    private sealed class BackgroundUpgrade : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;

        public BackgroundUpgrade(ThreadStore threadStore) => _run = Task.Run(() => RunAsync(threadStore, _stop.Token));

        private static async Task RunAsync(ThreadStore threadStore, CancellationToken ct)
        {
            try
            {
                await ApplyInBackgroundAsync(threadStore, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Background workspace upgrade failed: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _run.ConfigureAwait(false);
            _stop.Dispose();
        }
    }
}
