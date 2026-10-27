using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DotCraft.Sessions;

public sealed record RolloutItemLine(int LineNumber, DateTimeOffset Timestamp, string TurnId, SessionItem Item);

public static class RolloutItemReader
{
    public static async IAsyncEnumerable<RolloutItemLine> ReadAsync(
        string path,
        Func<string, bool>? lineFilter = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        var lineNumber = 0;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (lineFilter != null && !lineFilter(line))
                continue;

            ThreadRolloutRecord? record;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("kind", out var kind)
                    || kind.GetString() is not (RolloutKinds.ItemAppended or RolloutKinds.TurnStateReplaced))
                {
                    continue;
                }

                record = document.RootElement.Deserialize<ThreadRolloutRecord>(SessionJsonOptions.Default);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                continue;
            }

            if (record?.ItemAppended is { } appended)
            {
                yield return new RolloutItemLine(lineNumber, record.Timestamp, appended.TurnId, appended.Item);
            }
            else if (record?.TurnStateReplaced is { } replacement)
            {
                foreach (var item in replacement.Turn.Items)
                    yield return new RolloutItemLine(lineNumber, record.Timestamp, replacement.Turn.Id, item);
            }
        }
    }
}
