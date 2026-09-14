using System.Runtime.CompilerServices;
using System.Text;

namespace DotCraft.Agents;

internal static class ResponsesSseReader
{
    internal static async IAsyncEnumerable<string> ReadDataAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                    yield return data.ToString();
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.AsSpan(5);
                if (value.Length > 0 && value[0] == ' ')
                    value = value[1..];
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(value);
            }
        }
        if (data.Length > 0)
            yield return data.ToString();
    }
}
