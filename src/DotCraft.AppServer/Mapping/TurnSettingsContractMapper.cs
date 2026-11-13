using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal static class TurnSettingsContractMapper
{
    internal static TurnSettingsOverrides ToDomain(Contract.TurnStartParams value)
    {
        var reasoning = value.Reasoning;
        return new TurnSettingsOverrides
        {
            ProviderId = value.ProviderId,
            Model = value.Model,
            Mode = value.Mode,
            Cwd = value.Cwd,
            RuntimeWorkspaceRoots = value.RuntimeWorkspaceRoots,
            Speed = Parse<InferenceSpeed>(value.Speed, "speed"),
            Reasoning = reasoning is null ? null : new TurnReasoningOverrides
            {
                Enabled = reasoning.Enabled.IsSet ? reasoning.Enabled.Value : null,
                Effort = Parse<ModelReasoningEffort>(reasoning.Effort.IsSet ? reasoning.Effort.Value : null, "reasoning.effort"),
                Output = Parse<ReasoningOutput>(reasoning.Output.IsSet ? reasoning.Output.Value : null, "reasoning.output")
            }
        };
    }

    private static T? Parse<T>(string? value, string name) where T : struct, Enum
    {
        if (value is null)
            return null;
        foreach (var candidate in Enum.GetNames<T>())
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<T>(candidate);
        }
        throw AppServerErrors.InvalidParams($"'{name}' is not a supported value.");
    }
}
