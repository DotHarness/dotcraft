using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence.Upgrades;

internal interface IWorkspaceUpgrade
{
    string Version { get; }

    void Apply(SqliteConnection connection, string dataPath);
}

/// <summary>
/// Brings an existing workspace's data to the current release once, recording the release in
/// <c>workspace_meta</c>. Only the current release's upgrade is kept here; the next release removes it.
/// </summary>
internal static class WorkspaceUpgrades
{
    private const string DataVersionKey = "data_version";

    private static readonly IWorkspaceUpgrade? Current = new WorkspaceUpgrade_0_9_2();

    public static void Apply(SqliteConnection connection)
    {
        if (Current == null || string.Equals(ReadDataVersion(connection), Current.Version, StringComparison.Ordinal))
            return;

        Current.Apply(connection, Path.GetDirectoryName(Path.GetFullPath(connection.DataSource))!);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspace_meta (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", DataVersionKey);
        command.Parameters.AddWithValue("$value", Current.Version);
        command.ExecuteNonQuery();
    }

    private static string? ReadDataVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM workspace_meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", DataVersionKey);
        return command.ExecuteScalar() as string;
    }
}
