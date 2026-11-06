using System.Text.Json;
using DotCraft.Plugins;

namespace DotCraft.CLI;

internal static class PluginCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> VerifyAsync(
        string candidatePath,
        bool forPublish,
        bool json,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var result = await PluginVerifier.VerifyAsync(
            new PluginVerificationRequest(candidatePath, forPublish),
            cancellationToken).ConfigureAwait(false);

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
            return result.IsValid ? 0 : 1;
        }

        await output.WriteLineAsync(result.IsValid
            ? $"Plugin is valid: {result.PluginId}"
            : $"Plugin is invalid: {result.CandidatePath}").ConfigureAwait(false);
        foreach (var item in result.Diagnostics)
        {
            var location = item.Path == null ? string.Empty : $" ({item.Path})";
            await output.WriteLineAsync($"- {item.Severity} {item.Code}: {item.Message}{location}").ConfigureAwait(false);
        }

        return result.IsValid ? 0 : 1;
    }
}
