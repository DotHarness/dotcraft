using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesAttemptDiagnosticPipelinePolicy : PipelinePolicy
{
    private const string ResponsesPathSuffix = "/responses";

    private static readonly string[] RequestIdHeaders =
    [
        "x-request-id",
        "request-id",
        "openai-request-id"
    ];

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (!ShouldCapture(message))
        {
            ProcessNext(message, pipeline, currentIndex);
            return;
        }

        var routingIdentity = OpenAIResponsesCodexMetadata.ResolveRoutingIdentity();
        try
        {
            ProcessNext(message, pipeline, currentIndex);
        }
        finally
        {
            Capture(message, routingIdentity);
        }
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (!ShouldCapture(message))
        {
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
            return;
        }

        var routingIdentity = OpenAIResponsesCodexMetadata.ResolveRoutingIdentity();
        try
        {
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        }
        finally
        {
            Capture(message, routingIdentity);
        }
    }

    private static bool ShouldCapture(PipelineMessage message) =>
        ModelStreamAttemptRuntimeScope.Current != null
        && message.Request.Uri is { } uri
        && uri.AbsolutePath.EndsWith(ResponsesPathSuffix, StringComparison.Ordinal);

    private static void Capture(
        PipelineMessage message,
        OpenAIResponsesRoutingIdentity routingIdentity)
    {
        var context = ModelStreamAttemptRuntimeScope.Current;
        if (context == null)
            return;

        var response = message.Response;
        context.CaptureOpenAIResponse(
            response?.Status,
            ReadRequestId(response),
            ComputeHash(routingIdentity.SessionId),
            ComputeHash(routingIdentity.ThreadId),
            ComputeHash(routingIdentity.DefaultPromptCacheKey));
    }

    private static string? ReadRequestId(PipelineResponse? response)
    {
        if (response == null)
            return null;

        foreach (var header in RequestIdHeaders)
        {
            if (response.Headers.TryGetValue(header, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? ComputeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
