using DotCraft.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.Persistence;

public sealed class WorkspaceStateInitializationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WorkspaceStateInit_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WritableInitialization_ReportsEachStageOnce_AndReadOnlyOpensDoNotInitialize()
    {
        var logger = new StartupLogger();
        using var database = new WorkspaceStateDatabase(_root, readOnly: false, logger);
        using (database.OpenConnection()) { }
        using (database.OpenConnection()) { }

        Assert.Equal(
            ["workspace-state-open", "workspace-state-schema", "workspace-state-columns", "workspace-state-indexes"],
            logger.Entries.Select(entry => entry.Stage));
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Information, entry.Level));

        logger.Entries.Clear();
        using var readOnly = new WorkspaceStateDatabase(_root, readOnly: true, logger);
        using (readOnly.OpenConnection()) { }
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void FailedIndexInitialization_ReportsTheFailedStageAndPreservesTheException()
    {
        using (var database = new WorkspaceStateDatabase(_root))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP INDEX idx_thread_items_summary;
                CREATE VIEW idx_thread_items_summary AS SELECT 1;
                """;
            command.ExecuteNonQuery();
        }

        var logger = new StartupLogger();
        var error = Assert.Throws<SqliteException>(() => new WorkspaceStateDatabase(_root, readOnly: false, logger));

        var failed = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("workspace-state-indexes", failed.Stage);
        Assert.Same(error, failed.Exception);
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "state.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString());
        SqliteConnection.ClearPool(connection);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class StartupLogger : ILogger<WorkspaceStateDatabase>
    {
        public List<StageLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(entry => entry.Key, entry => entry.Value);
            Entries.Add(new StageLog(logLevel, (string)fields["Stage"]!, (double)fields["ElapsedMs"]!, exception));
        }
    }

    private sealed record StageLog(LogLevel Level, string Stage, double ElapsedMs, Exception? Exception);
}
