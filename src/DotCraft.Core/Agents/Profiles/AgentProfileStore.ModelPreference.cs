using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class AgentProfileStore
{
    private static void ApplyProviderPreference(
        ThreadConfiguration config,
        AgentProfileProviderPreference profilePreference,
        AppConfig appConfig)
    {
        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveMain(
                appConfig,
                profilePreference.ProviderId,
                profilePreference.Model);
        }
        catch (Exception ex) when (ex is ArgumentException or ModelProviderConfigurationException)
        {
            throw new AgentProfileException(
                AgentProfileErrorKind.ValidationFailed,
                $"Pinned provider '{profilePreference.ProviderId}' is not runnable in the current workspace.",
                [Error("PinnedProviderUnavailable", $"Pinned provider '{profilePreference.ProviderId}' is not runnable in the current workspace.")]);
        }

        var capability = ModelThinkingAdapterResolver.ResolveReasoningCapability(
            appConfig,
            runtime.Protocol,
            runtime.EndPoint,
            runtime.Model);
        var preference = ModelPreferenceRules.Normalize(
            appConfig,
            runtime.ProviderId,
            new ModelPreference
            {
                Model = runtime.Model,
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = profilePreference.Reasoning.Enabled,
                    Effort = profilePreference.Reasoning.Effort,
                    Output = capability?.DefaultOutput ?? ReasoningOutput.Full
                },
                Speed = profilePreference.Speed,
            });

        config.ProviderId = runtime.ProviderId;
        config.Model = preference.Model;
        config.Reasoning = CloneReasoning(preference.Reasoning);
        config.Speed = preference.Speed;

    }

    private static AgentProfileProviderPreference? CompileProviderPreference(
        JsonObject frontmatter,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (!TryGetProperty(frontmatter, "providerPreference", out _))
            return null;

        var section = TryGetObject(frontmatter, "providerPreference", diagnostics);
        if (section == null)
            return null;

        RequireProperties(
            section,
            "providerPreference",
            diagnostics,
            "providerId",
            "model",
            "reasoning",
            "speed");

        var reasoningSection = TryGetObject(section, "reasoning", diagnostics);
        if (reasoningSection != null)
        {
            RequireProperties(
                reasoningSection,
                "providerPreference.reasoning",
                diagnostics,
                "enabled",
                "effort");
        }


        var providerId = NormalizeNullableString(ReadOptionalString(section, "providerId", diagnostics, required: true));
        var model = NormalizeNullableString(ReadOptionalString(section, "model", diagnostics, required: true));
        var reasoning = CompileProfileReasoning(reasoningSection, diagnostics) ?? new AgentProfileReasoningPreference();

        return new AgentProfileProviderPreference
        {
            ProviderId = providerId ?? string.Empty,
            Model = model ?? string.Empty,
            Reasoning = reasoning,
            Speed = ParseInferenceSpeed(ReadOptionalString(section, "speed", diagnostics), diagnostics),

        };
    }

    private static void RequireProperties(
        JsonObject section,
        string path,
        List<AgentProfileDiagnostic> diagnostics,
        params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!TryGetProperty(section, property, out var value) || value == null)
            {
                diagnostics.Add(Error(
                    "MissingRequiredField",
                    $"Agent profile field '{path}.{property}' is required."));
            }
        }
    }

    private static InferenceSpeed ParseInferenceSpeed(
        string? raw,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.Equals(raw, "fast", StringComparison.OrdinalIgnoreCase))
            return InferenceSpeed.Fast;
        if (string.Equals(raw, "standard", StringComparison.OrdinalIgnoreCase))
            return InferenceSpeed.Standard;

        if (!string.IsNullOrWhiteSpace(raw))
            diagnostics.Add(Error("InvalidPolicyValue", "providerPreference.speed must be standard or fast."));
        return InferenceSpeed.Standard;
    }

    private static AgentProfileReasoningPreference? CompileProfileReasoning(
        JsonObject? reasoning,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (reasoning == null)
            return null;

        var enabled = ReadOptionalBool(reasoning, "enabled", diagnostics);
        var effort = ReadOptionalString(reasoning, "effort", diagnostics);
        return new AgentProfileReasoningPreference
        {
            Enabled = enabled ?? true,
            Effort = ParseReasoningEffort(effort, diagnostics) ?? ModelReasoningEffort.Medium
        };
    }

    private static ModelReasoningEffort? ParseReasoningEffort(string? raw, List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = NormalizeEnumToken(raw);
        return normalized switch
        {
            "low" => ModelReasoningEffort.Low,
            "medium" => ModelReasoningEffort.Medium,
            "high" => ModelReasoningEffort.High,
            "extrahigh" or "xhigh" => ModelReasoningEffort.ExtraHigh,
            "ultra" => ModelReasoningEffort.Ultra,
            "max" => ModelReasoningEffort.Max,
            _ => AddReasoningEffortError(diagnostics)
        };
    }

    private static ModelReasoningEffort? AddReasoningEffortError(List<AgentProfileDiagnostic> diagnostics)
    {
        diagnostics.Add(Error(
            "InvalidPolicyValue",
            "providerPreference.reasoning.effort must be low, medium, high, extraHigh, max, or ultra; use enabled: false for Off."));
        return null;
    }

}
