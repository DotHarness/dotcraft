using System.ClientModel.Primitives;

namespace DotCraft.Agents;

internal sealed class DotCraftUserAgentPipelinePolicy : PipelinePolicy
{
    internal static readonly string UserAgentValue = BuildUserAgent();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Apply(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static void Apply(PipelineMessage message)
        => message.Request.Headers.Set("User-Agent", UserAgentValue);

    private static string BuildUserAgent()
    {
        var version = typeof(DotCraftUserAgentPipelinePolicy).Assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "DotCraft/0.0.0" : $"DotCraft/{version}";
    }
}
