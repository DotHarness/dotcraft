using System.ClientModel.Primitives;

namespace DotCraft.Agents;

/// <summary>
/// Marks ChatGPT OAuth Responses requests for the provider's Responses Lite contract.
/// </summary>
internal sealed class OpenAIResponsesLiteHeadersPipelinePolicy : PipelinePolicy
{
    internal const string ResponsesLiteHeader = "x-openai-internal-codex-responses-lite";
    internal const string AcceptHeader = "Accept";
    internal const string EventStreamContentType = "text/event-stream";

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
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

    internal static bool Apply(PipelineMessage message)
    {
        OpenAIResponsesLitePipelinePath.EnsureSupported(message.Request.Uri);

        message.Request.Headers.Set(ResponsesLiteHeader, "true");
        if (OpenAIResponsesLitePipelinePath.IsStreamingResponses(message.Request.Uri))
            message.Request.Headers.Set(AcceptHeader, EventStreamContentType);
        return true;
    }
}

internal static class OpenAIResponsesLitePipelinePath
{
    internal static void EnsureSupported(Uri? uri)
    {
        if (!IsSupported(uri))
        {
            throw new InvalidOperationException(
                "Responses Lite pipeline only supports /responses and /responses/compact requests.");
        }
    }

    internal static bool IsSupported(Uri? uri)
    {
        if (uri is null)
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        return path.EndsWith("/responses", StringComparison.Ordinal)
            || path.EndsWith("/responses/compact", StringComparison.Ordinal);
    }

    internal static bool IsStreamingResponses(Uri? uri)
    {
        if (uri is null)
            return false;

        return uri.AbsolutePath.TrimEnd('/').EndsWith("/responses", StringComparison.Ordinal);
    }
}
