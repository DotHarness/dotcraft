using DotCraft.CodeMode;
using DotCraft.DynamicWorkflows;

namespace DotCraft.CLI;

internal static class ScriptWorkers
{
    private static readonly Dictionary<string, Func<Stream, Stream, Stream, CancellationToken, Task<int>>> Runners =
        new(StringComparer.Ordinal)
        {
            ["workflow"] = WorkflowWorkerRunner.RunAsync,
            ["code-mode"] = CodeModeWorkerRunner.RunAsync
        };

    public static async Task<int> RunAsync(string kind, CancellationToken cancellationToken)
    {
        if (!Runners.TryGetValue(kind, out var runner))
        {
            await Console.Error.WriteLineAsync(
                $"Unknown script worker kind '{kind}'. Expected one of: {string.Join(", ", Runners.Keys)}.").ConfigureAwait(false);
            return 2;
        }
        return await runner(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            Console.OpenStandardError(),
            cancellationToken).ConfigureAwait(false);
    }
}
