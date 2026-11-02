using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionProviderFailure
{
    internal static string Reason(IChatClient client, Exception exception)
    {
        var classifier = client.GetService<IProviderFailureClassifier>() ?? DefaultProviderFailureClassifier.Instance;
        var failure = classifier.ClassifyCaptured(exception);
        return failure.Kind == ProviderFailureKind.Other
            ? "provider_error"
            : "provider_" + JsonNamingPolicy.SnakeCaseLower.ConvertName(failure.Kind.ToString());
    }
}

internal sealed class CompactionSummaryException(string reason, Exception? inner = null) : Exception(reason, inner);
