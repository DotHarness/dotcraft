using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed record PreparedTurnSettings(ThreadConfiguration Configuration, bool ResourcesChanged);

    private PreparedTurnSettings? PrepareTurnSettings(
        SessionThread thread, TurnSettingsOverrides? settings, string? inputMode)
    {
        var mode = settings?.Mode ?? inputMode;
        if (mode is not null && mode is not ("agent" or "plan"))
            throw new ArgumentException("Mode must be 'agent' or 'plan'.", nameof(settings));
        ValidateOptionalIdentifier(settings?.ProviderId, "providerId");
        ValidateOptionalIdentifier(settings?.Model, "model");

        var current = thread.Configuration ?? new ThreadConfiguration();
        var candidate = ThreadConfigurationCloner.Clone(current);
        if (settings is not null && (settings.ProviderId is not null || settings.Model is not null
            || settings.Reasoning is not null || settings.Speed.HasValue))
        {
            ApplyTurnModelSettings(candidate, settings);
        }
        if (mode is not null)
            candidate.Mode = mode;
        ThreadWorkspaceResolver.Apply(thread.WorkspacePath, candidate, settings?.Cwd, settings?.RuntimeWorkspaceRoots);

        var resourcesChanged = !SameTurnModelSettings(current, candidate)
            || !string.Equals(current.Cwd, candidate.Cwd, PathComparison)
            || !RootsEqual(current.RuntimeWorkspaceRoots, candidate.RuntimeWorkspaceRoots);
        return resourcesChanged || !string.Equals(current.Mode, candidate.Mode, StringComparison.Ordinal)
            ? new(candidate, resourcesChanged)
            : null;
    }

    private void ApplyTurnModelSettings(ThreadConfiguration candidate, TurnSettingsOverrides settings)
    {
        var appConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var providerId = settings.ProviderId?.Trim() ?? candidate.ProviderId;
        var providerChanged = settings.ProviderId is not null
            && !string.Equals(providerId, candidate.ProviderId, StringComparison.OrdinalIgnoreCase);
        if (providerChanged && settings.Model is null)
            throw new ArgumentException("Changing provider requires an explicit model.", nameof(settings));
        EffectiveModelRuntime runtime;
        try
        {
            runtime = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
                appConfig, providerId, settings.Model?.Trim() ?? candidate.Model);
        }
        catch (ModelProviderConfigurationException error)
        {
            throw new ArgumentException(error.Message, nameof(settings), error);
        }
        var preference = providerChanged
            ? ModelPreferenceRules.Find(appConfig.ProviderPreferences, runtime.ProviderId)
                ?? ModelPreferenceRules.CreateDefault(appConfig, runtime.ProviderId, runtime.Model)
            : new ModelPreference
            {
                Model = runtime.Model,
                Reasoning = ThreadConfigurationCloner.CloneReasoningConfig(candidate.Reasoning ?? appConfig.Reasoning),
                Speed = candidate.Speed ?? InferenceSpeed.Standard
            };
        preference.Model = runtime.Model;
        preference = ModelPreferenceRules.Normalize(appConfig, runtime.ProviderId, preference);
        if (settings.Reasoning is { } reasoning)
        {
            if (reasoning.Enabled.HasValue)
                preference.Reasoning.Enabled = reasoning.Enabled.Value;
            if (reasoning.Effort.HasValue)
                preference.Reasoning.Effort = reasoning.Effort.Value;
            if (reasoning.Output.HasValue)
                preference.Reasoning.Output = reasoning.Output.Value;
            if (ModelPreferenceRules.ValidateReasoning(
                appConfig, runtime.ProviderId, runtime.Model, preference.Reasoning) is { } error)
                throw new ArgumentException(error, nameof(settings));
        }
        if (settings.Speed.HasValue)
            preference.Speed = settings.Speed.Value;
        preference = ModelPreferenceRules.Normalize(appConfig, runtime.ProviderId, preference);
        candidate.ProviderId = runtime.ProviderId;
        candidate.Model = preference.Model;
        candidate.Reasoning = preference.Reasoning;
        candidate.Speed = preference.Speed;
    }

    private async Task CommitTurnSettingsAsync(ThreadRuntime runtime, PreparedTurnSettings? prepared, CancellationToken ct)
    {
        if (prepared is null)
            return;
        ct.ThrowIfCancellationRequested();
        var thread = runtime.Thread;
        if (thread.Ephemeral)
            thread.Configuration = prepared.Configuration;
        else
        {
            var captured = CaptureRunningTurnItems(thread);
            await persistence.CommitThreadConfigurationAsync(thread, prepared.Configuration, ct);
            SetMaterializedIfCurrent(thread, materialized: true);
            MarkTurnItemsPersisted(captured);
            ThreadWorkingSet.Trim(thread);
        }
        if (prepared.ResourcesChanged)
        {
            runtime.Agent = null;
            runtime.MarkToolSnapshotDirty();
        }
        if (!thread.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
            && runtime.ModeManager is { } modeManager)
            modeManager.SwitchMode(prepared.Configuration.Mode == "plan" ? AgentMode.Plan : AgentMode.Agent);
        ThreadUpdatedForBroadcast?.Invoke(thread);
    }

    private static bool SameTurnModelSettings(ThreadConfiguration left, ThreadConfiguration right) =>
        string.Equals(left.ProviderId, right.ProviderId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Model, right.Model, StringComparison.Ordinal)
        && left.Speed == right.Speed
        && left.Reasoning?.Enabled == right.Reasoning?.Enabled
        && left.Reasoning?.Effort == right.Reasoning?.Effort
        && left.Reasoning?.Output == right.Reasoning?.Output;

    private static void ValidateOptionalIdentifier(string? value, string name)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' must not be empty.", name);
    }
}
