using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

/// <summary>
/// Complete provider-scoped model preference captured by newly created threads.
/// </summary>
public sealed class ModelPreference
{
    /// <summary>Selected model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Provider-neutral reasoning selection.</summary>
    public AppConfig.ReasoningConfig Reasoning { get; set; } = new();

    /// <summary>Requested inference-speed mode.</summary>
    public InferenceSpeed Speed { get; set; } = InferenceSpeed.Standard;

    /// <summary>Requested context-window mode.</summary>
    public ModelPreferenceContextWindow ContextWindow { get; set; } = new();
}

/// <summary>Context-window selection stored in a <see cref="ModelPreference"/>.</summary>
public sealed class ModelPreferenceContextWindow
{
    /// <summary>Selected context-window mode.</summary>
    public ContextWindowMode Mode { get; set; } = ContextWindowMode.Default;
}

/// <summary>Normalization and capability-safe operations for <see cref="ModelPreference"/>.</summary>
public static class ModelPreferenceRules
{
    /// <summary>Creates the safe no-catalog fallback for a manually entered model.</summary>
    public static ModelPreference CreateManual(string model) => new()
    {
        Model = NormalizeRequiredModel(model),
        Reasoning = new AppConfig.ReasoningConfig
        {
            Enabled = false,
            Effort = ReasoningEffort.Medium,
            Output = ReasoningOutput.Full
        },
        Speed = InferenceSpeed.Standard,
        ContextWindow = new ModelPreferenceContextWindow { Mode = ContextWindowMode.Default }
    };

    /// <summary>Creates a capability-safe preference for a configured provider and model.</summary>
    public static ModelPreference CreateDefault(AppConfig config, string providerId, string model)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = CreateManual(model);
        if (!TryResolveRuntime(config, providerId, normalized.Model, out var runtime))
            return normalized;

        var reasoning = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            config,
            runtime.Protocol,
            runtime.EndPoint,
            normalized.Model);
        if (reasoning is { SupportsDisable: false })
        {
            normalized.Reasoning.Enabled = true;
            normalized.Reasoning.Effort = reasoning.DefaultEffort;
            normalized.Reasoning.Output = reasoning.DefaultOutput;
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes a preference and repairs selections that are invalid for its model.
    /// </summary>
    public static ModelPreference Normalize(AppConfig config, string providerId, ModelPreference preference)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(preference);

        var normalized = Clone(preference);
        normalized.Model = NormalizeRequiredModel(normalized.Model);
        normalized.Reasoning ??= new AppConfig.ReasoningConfig();
        normalized.ContextWindow ??= new ModelPreferenceContextWindow();

        if (!TryResolveRuntime(config, providerId, normalized.Model, out var runtime))
            return normalized;

        var reasoning = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            config,
            runtime.Protocol,
            runtime.EndPoint,
            normalized.Model);
        if (reasoning != null)
        {
            if (!normalized.Reasoning.Enabled && !reasoning.SupportsDisable)
            {
                normalized.Reasoning.Enabled = true;
                normalized.Reasoning.Effort = reasoning.DefaultEffort;
                normalized.Reasoning.Output = reasoning.DefaultOutput;
            }
            else if (normalized.Reasoning.Enabled)
            {
                if (reasoning.SupportedEfforts.All(option => option.Effort != normalized.Reasoning.Effort))
                    normalized.Reasoning.Effort = reasoning.DefaultEffort;
                if (!reasoning.SupportedOutputs.Contains(normalized.Reasoning.Output))
                    normalized.Reasoning.Output = reasoning.DefaultOutput;
            }
        }

        if (normalized.ContextWindow.Mode == ContextWindowMode.Max
            && !ModelCatalog.ResolveContextWindowCapability(config, normalized.Model).SupportsMax)
        {
            normalized.ContextWindow.Mode = ContextWindowMode.Default;
        }

        return normalized;
    }

    /// <summary>Returns a deep copy of a preference.</summary>
    public static ModelPreference Clone(ModelPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        return new ModelPreference
        {
            Model = preference.Model,
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = preference.Reasoning?.Enabled ?? false,
                Effort = preference.Reasoning?.Effort ?? ReasoningEffort.Medium,
                Output = preference.Reasoning?.Output ?? ReasoningOutput.Full
            },
            Speed = preference.Speed,
            ContextWindow = new ModelPreferenceContextWindow
            {
                Mode = preference.ContextWindow?.Mode ?? ContextWindowMode.Default
            }
        };
    }

    /// <summary>Compares complete preference values.</summary>
    public static bool ValueEquals(ModelPreference? left, ModelPreference? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return string.Equals(left.Model?.Trim(), right.Model?.Trim(), StringComparison.Ordinal)
            && left.Reasoning?.Enabled == right.Reasoning?.Enabled
            && left.Reasoning?.Effort == right.Reasoning?.Effort
            && left.Reasoning?.Output == right.Reasoning?.Output
            && left.Speed == right.Speed
            && left.ContextWindow?.Mode == right.ContextWindow?.Mode;
    }

    /// <summary>Finds and clones a provider preference using a case-insensitive provider id.</summary>
    public static ModelPreference? Find(
        IReadOnlyDictionary<string, ModelPreference>? preferences,
        string? providerId)
    {
        if (preferences is null || string.IsNullOrWhiteSpace(providerId))
            return null;

        foreach (var (key, value) in preferences)
        {
            if (string.Equals(key, providerId.Trim(), StringComparison.OrdinalIgnoreCase)
                && value is not null
                && !string.IsNullOrWhiteSpace(value.Model))
            {
                return Clone(value);
            }
        }

        return null;
    }

    /// <summary>Normalizes provider keys and drops incomplete records.</summary>
    public static Dictionary<string, ModelPreference> NormalizeMap(
        AppConfig config,
        IReadOnlyDictionary<string, ModelPreference>? preferences)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase);
        if (preferences is null)
            return result;

        foreach (var (rawProviderId, preference) in preferences)
        {
            var providerId = rawProviderId?.Trim();
            if (string.IsNullOrEmpty(providerId) || preference is null || string.IsNullOrWhiteSpace(preference.Model))
                continue;
            result[providerId] = Normalize(config, providerId, preference);
        }

        return result;
    }

    private static string NormalizeRequiredModel(string model)
    {
        var normalized = model?.Trim();
        if (string.IsNullOrEmpty(normalized) || string.Equals(normalized, "Default", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Model must be configured.", nameof(model));
        return normalized;
    }

    private static bool TryResolveRuntime(
        AppConfig config,
        string providerId,
        string model,
        out EffectiveModelRuntime runtime)
    {
        try
        {
            runtime = ModelProviderResolver.ResolveMain(config, providerId, model);
            return true;
        }
        catch (ArgumentException)
        {
            runtime = null!;
            return false;
        }
        catch (ModelProviderConfigurationException)
        {
            runtime = null!;
            return false;
        }
    }
}
