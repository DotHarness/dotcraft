using System.Text;

namespace DotCraft.CLI;

public sealed record ExecRequest
{
    public required string WorkspacePath { get; init; }
    public required string Prompt { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<string> Images { get; init; } = [];
    public bool AutoApprove { get; init; }
    public bool Ephemeral { get; init; }
    public bool Json { get; init; }
    public string? OutputLastMessagePath { get; init; }
    public string? ResumeThreadId { get; init; }
    public bool ResumeLast { get; init; }
}

internal sealed class ExecUsageException(string message) : Exception(message);

internal static class ExecInput
{
    private const string NoPromptMessage =
        "No prompt provided. Either specify one as an argument or pipe the prompt into stdin.";

    public static async Task<string> ResolvePromptAsync(
        IReadOnlyList<string> promptTokens,
        CancellationToken ct)
    {
        var stdinRedirected = Console.IsInputRedirected;
        if (promptTokens.Count == 0 || promptTokens is ["-"])
        {
            if (promptTokens.Count == 0 && !stdinRedirected)
                throw new ExecUsageException(NoPromptMessage);

            var piped = (await ReadStandardInputAsync(ct).ConfigureAwait(false)).Trim();
            return piped.Length > 0 ? piped : throw new ExecUsageException(NoPromptMessage);
        }

        var prompt = string.Join(' ', promptTokens).Trim();
        if (prompt.Length == 0)
            throw new ExecUsageException("Prompt cannot be empty.");
        if (!stdinRedirected)
            return prompt;

        var stdin = await ReadStandardInputAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stdin))
            return prompt;

        var newline = stdin.EndsWith('\n') ? string.Empty : "\n";
        return $"{prompt}\n\n<stdin>\n{stdin}{newline}</stdin>";
    }

    public static IReadOnlyList<string> ResolveImages(IEnumerable<string> values)
    {
        var images = new List<string>();
        foreach (var value in values)
        {
            foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var path = Path.GetFullPath(entry);
                if (!File.Exists(path))
                    throw new ExecUsageException($"Image file not found: {path}");
                images.Add(path);
            }
        }

        return images;
    }

    private static async Task<string> ReadStandardInputAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
