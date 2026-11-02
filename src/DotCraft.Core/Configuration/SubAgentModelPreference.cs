using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>
/// A native SubAgent model preference keyed by the parent's provider; a null <see cref="ProviderId"/> means that key.
/// </summary>
public sealed class SubAgentModelPreference
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }

    public string Model { get; set; } = string.Empty;

    public AppConfig.ReasoningConfig Reasoning { get; set; } = new();

    public InferenceSpeed Speed { get; set; } = InferenceSpeed.Standard;

    public ModelPreference ToModelPreference() => ModelPreferenceRules.Clone(new ModelPreference
    {
        Model = Model,
        Reasoning = Reasoning,
        Speed = Speed
    });

    public static SubAgentModelPreference From(string? providerId, ModelPreference preference)
    {
        var clone = ModelPreferenceRules.Clone(preference);
        return new SubAgentModelPreference
        {
            ProviderId = string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim(),
            Model = clone.Model,
            Reasoning = clone.Reasoning,
            Speed = clone.Speed
        };
    }

    public SubAgentModelPreference Clone() => From(ProviderId, ToModelPreference());

    public static SubAgentModelPreference? Find(
        IReadOnlyDictionary<string, SubAgentModelPreference>? preferences,
        string? parentProviderId)
    {
        if (preferences is null || string.IsNullOrWhiteSpace(parentProviderId))
            return null;
        var key = parentProviderId.Trim();
        foreach (var (candidate, value) in preferences)
        {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)
                && value is not null
                && !string.IsNullOrWhiteSpace(value.Model))
            {
                return From(value.ProviderId ?? key, value.ToModelPreference());
            }
        }
        return null;
    }
}

public static class SubAgentModelChoiceRules
{
    public const int MaxChoices = 8;

    /// <summary>Only entries the write adds must name a known provider, so a removed provider does not block other edits.</summary>
    public static string? Validate(
        IReadOnlyList<SubAgentModelChoice> previous,
        IReadOnlyList<SubAgentModelChoice> candidate,
        Func<string, bool> providerExists)
    {
        if (candidate.Count > MaxChoices)
            return $"'SubAgent.Models' holds at most {MaxChoices} entries.";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in candidate)
        {
            var providerId = choice.ProviderId?.Trim() ?? string.Empty;
            var model = choice.Model?.Trim() ?? string.Empty;
            if (providerId.Length == 0 || model.Length == 0)
                return "Each 'SubAgent.Models' entry needs a ProviderId and a Model.";
            if (!seen.Add($"{providerId}/{model}"))
                return $"'SubAgent.Models' lists '{providerId}/{model}' more than once.";
            var existed = previous.Any(old =>
                string.Equals(old.ProviderId?.Trim(), providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(old.Model?.Trim(), model, StringComparison.OrdinalIgnoreCase));
            if (!existed && !providerExists(providerId))
                return $"Model provider '{providerId}' is not configured.";
        }
        return null;
    }
}

public sealed class SubAgentModelChoice
{
    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}
