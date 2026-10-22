using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence;

internal static class StoredRolloutPath
{
    public static string ToStored(SqliteConnection connection, string rolloutPath) =>
        Path.GetRelativePath(DataPath(connection), Path.GetFullPath(rolloutPath));

    public static string Resolve(SqliteConnection connection, string storedPath) =>
        Path.GetFullPath(Path.Combine(DataPath(connection), storedPath));

    private static string DataPath(SqliteConnection connection) =>
        Path.GetDirectoryName(Path.GetFullPath(connection.DataSource))!;
}
