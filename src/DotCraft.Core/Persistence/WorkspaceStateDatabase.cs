using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence;

/// <summary>
/// Manages the shared SQLite state database for a DotCraft workspace.
/// </summary>
public sealed class WorkspaceStateDatabase : IDisposable
{
    private const double DefaultCompactFreelistRatio = 0.25;
    private const int DefaultCompactMinFreelistPages = 32;

    private readonly string _connectionString;
    private readonly bool _readOnly;
    private readonly object _initLock = new();
    private bool _initialized;

    public WorkspaceStateDatabase(string botPath)
        : this(botPath, readOnly: false)
    {
    }

    /// <summary>
    /// Creates a workspace state database.
    /// </summary>
    /// <param name="botPath">Path to the workspace <c>.craft</c> directory.</param>
    /// <param name="readOnly">
    /// When true, opens the existing state database without creating directories,
    /// creating schema or issuing write-oriented pragmas.
    /// </param>
    public WorkspaceStateDatabase(string botPath, bool readOnly)
    {
        _readOnly = readOnly;
        if (!readOnly)
            Directory.CreateDirectory(botPath);

        DbPath = Path.Combine(botPath, "state.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = readOnly ? SqliteCacheMode.Private : SqliteCacheMode.Shared
        }.ToString();

        if (!readOnly)
            EnsureInitialized();
    }

    public string DbPath { get; }

    public SqliteConnection OpenConnection()
    {
        if (!_readOnly)
            EnsureInitialized();

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = _readOnly
            ? """
              PRAGMA query_only=ON;
              PRAGMA foreign_keys=ON;
              """
            : """
              PRAGMA journal_mode=WAL;
              PRAGMA synchronous=NORMAL;
              PRAGMA foreign_keys=ON;
              PRAGMA secure_delete=ON;
              """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Releases pooled connections associated with this workspace database.
    /// </summary>
    public void Dispose()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>
    /// Truncates the SQLite write-ahead log for this workspace state database.
    /// </summary>
    public void CheckpointWalTruncate()
    {
        EnsureWritable();
        using var connection = OpenConnection();
        CheckpointWalTruncate(connection);
    }

    /// <summary>
    /// Reclaims free SQLite pages when the database has enough reusable space to justify compaction.
    /// </summary>
    /// <returns><c>true</c> when VACUUM was executed; otherwise <c>false</c>.</returns>
    public bool CompactIfWorthwhile(
        bool force = false,
        double minFreelistRatio = DefaultCompactFreelistRatio,
        int minFreelistPages = DefaultCompactMinFreelistPages)
    {
        EnsureWritable();
        using var connection = OpenConnection();
        var pageCount = ReadPragmaLong(connection, "page_count");
        var freelistCount = ReadPragmaLong(connection, "freelist_count");
        var ratio = pageCount <= 0 ? 0 : (double)freelistCount / pageCount;
        var shouldCompact = force
            || (freelistCount >= minFreelistPages && ratio >= minFreelistRatio);

        if (!shouldCompact)
        {
            CheckpointWalTruncate(connection);
            return false;
        }

        using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "VACUUM";
            vacuum.ExecuteNonQuery();
        }

        CheckpointWalTruncate(connection);
        return true;
    }

    private void EnsureWritable()
    {
        if (_readOnly)
            throw new InvalidOperationException("This state runtime is read-only.");
    }

    private void EnsureInitialized()
    {
        if (_readOnly || _initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            WorkspaceStateSchema.EnsureInitialized(connection);
            _initialized = true;
        }
    }

    private static long ReadPragmaLong(SqliteConnection connection, string pragmaName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName}";
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    private static void CheckpointWalTruncate(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Drain the pragma result set.
        }
    }
}
