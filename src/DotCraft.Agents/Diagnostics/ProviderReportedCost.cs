using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

/// <summary>Carries the USD amount a provider bills for one model request on its final response or update.</summary>
internal static class ProviderReportedCost
{
    public const string Key = "dotcraft.costUsd";

    public static void Attach(ChatResponseUpdate update, decimal amount) =>
        (update.AdditionalProperties ??= new AdditionalPropertiesDictionary())[Key] = amount;

    public static void Attach(ChatResponse response, decimal amount) =>
        (response.AdditionalProperties ??= new AdditionalPropertiesDictionary())[Key] = amount;

    public static decimal? TryGet(AdditionalPropertiesDictionary? properties) =>
        properties?.TryGetValue(Key, out var value) == true && value is decimal amount ? amount : null;
}
