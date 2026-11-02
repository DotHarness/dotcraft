using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Sessions;

internal static class SubAgentModelCatalogSnapshots
{
    internal const int MaxModelOverrides = 5;

    public static async Task<SubAgentModelCatalogSnapshot> CreateAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        string providerId,
        CancellationToken cancellationToken)
    {
        var items = config.SubAgent.Models.Count > 0
            ? await CreateConfiguredItemsAsync(config, providerRegistry, cancellationToken).ConfigureAwait(false)
            : await CreateCatalogItemsAsync(config, providerRegistry, providerId, cancellationToken).ConfigureAwait(false);
        return new SubAgentModelCatalogSnapshot
        {
            ProviderId = providerId,
            Models = items,
            Description = RenderDescription(items)
        };
    }

    private static async Task<List<SubAgentModelCatalogItem>> CreateCatalogItemsAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        string providerId,
        CancellationToken cancellationToken)
    {
        var result = await FetchCatalogAsync(config, providerRegistry, providerId, cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return result.Success
            ? result.Models
                .Where(model => model.Id.Trim() is { Length: > 0 } id && seen.Add(id))
                .Take(MaxModelOverrides)
                .Select(model => CreateItem(config, providerId, model.Id.Trim(), result, model))
                .ToList()
            : [];
    }

    private static async Task<List<SubAgentModelCatalogItem>> CreateConfiguredItemsAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        CancellationToken cancellationToken)
    {
        var catalogs = new Dictionary<string, ModelCatalogResult>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<SubAgentModelCatalogItem>();
        foreach (var choice in config.SubAgent.Models.Take(SubAgentModelChoiceRules.MaxChoices))
        {
            var providerId = choice.ProviderId?.Trim() ?? string.Empty;
            var model = choice.Model?.Trim() ?? string.Empty;
            if (model.Length == 0
                || !ModelProviderResolver.IsProviderUsable(config, providerId)
                || !seen.Add($"{providerId}/{model}"))
            {
                continue;
            }
            if (!catalogs.TryGetValue(providerId, out var result))
            {
                result = await FetchCatalogAsync(config, providerRegistry, providerId, cancellationToken).ConfigureAwait(false);
                catalogs[providerId] = result;
            }
            var entry = result.Success
                ? result.Models.FirstOrDefault(candidate => string.Equals(candidate.Id.Trim(), model, StringComparison.OrdinalIgnoreCase))
                : null;
            items.Add(CreateItem(config, providerId, model, result, entry));
        }
        return items;
    }

    private static async Task<ModelCatalogResult> FetchCatalogAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        string providerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ModelProviderCatalog.FetchAsync(
                config,
                providerRegistry,
                providerId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ModelCatalogResult { Success = false };
        }
    }

    public static SubAgentModelCatalogSnapshot Clone(SubAgentModelCatalogSnapshot source) => new()
    {
        ProviderId = source.ProviderId,
        Description = source.Description,
        Models = source.Models.Select(static model => new SubAgentModelCatalogItem
        {
            Id = model.Id,
            ProviderId = model.ProviderId,
            Model = model.Model,
            SupportedReasoningEfforts = [.. model.SupportedReasoningEfforts],
            DefaultReasoningEffort = model.DefaultReasoningEffort
        }).ToList()
    };

    public static string AppendToToolDescription(string baseDescription, SubAgentModelCatalogSnapshot? snapshot)
    {
        var fragment = snapshot?.Description;
        return string.IsNullOrWhiteSpace(fragment)
            ? baseDescription
            : $"{baseDescription.TrimEnd()}\n\n{fragment}";
    }

    public static SubAgentInvocationModelOverride? ResolveInvocationOverride(
        SubAgentModelCatalogSnapshot? snapshot,
        string? model,
        string? reasoningEffort,
        string? inheritedProviderId = null,
        string? inheritedModel = null)
    {
        var normalizedModel = NormalizeOptional(model);
        var normalizedEffort = NormalizeOptional(reasoningEffort);
        if (normalizedModel == null && normalizedEffort == null)
            return null;
        if (snapshot == null)
            throw new InvalidOperationException("Subagent model overrides are unavailable because this thread has no model catalog snapshot.");

        SubAgentModelCatalogItem? selected = null;
        if (normalizedModel != null)
        {
            selected = snapshot.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, normalizedModel, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
                throw new InvalidOperationException(BuildUnavailableModelMessage(snapshot));
        }
        else if (!string.IsNullOrWhiteSpace(inheritedModel))
        {
            selected = Find(snapshot, inheritedProviderId, inheritedModel);
        }

        ModelReasoningEffort? effort = null;
        if (normalizedEffort != null)
        {
            if (!TryParseEffort(normalizedEffort, out var parsed))
                throw new InvalidOperationException($"Unknown subagent reasoning effort '{normalizedEffort}'.");
            effort = parsed;
            if (selected != null
                && selected.SupportedReasoningEfforts.Count > 0
                && !selected.SupportedReasoningEfforts.Contains(parsed))
            {
                var supported = string.Join(", ", selected.SupportedReasoningEfforts.Select(ToModelToken));
                throw new InvalidOperationException(
                    $"Model '{selected.Id}' does not support reasoning effort '{normalizedEffort}'. Available efforts: {supported}.");
            }
        }

        return new SubAgentInvocationModelOverride
        {
            ProviderId = normalizedModel == null ? null : selected?.ProviderId,
            Model = normalizedModel == null ? null : selected?.Model,
            Effort = effort
        };
    }

    public static void ValidateInvocationOverride(
        SubAgentModelCatalogSnapshot snapshot,
        SubAgentInvocationModelOverride invocationOverride)
    {
        var model = NormalizeOptional(invocationOverride.Model);
        if (model == null)
            return;
        var selected = Find(snapshot, invocationOverride.ProviderId, model);
        if (selected == null)
            throw new InvalidOperationException(BuildUnavailableModelMessage(snapshot));
        if (invocationOverride.Effort is { } effort
            && selected.SupportedReasoningEfforts.Count > 0
            && !selected.SupportedReasoningEfforts.Contains(effort))
        {
            var supported = string.Join(", ", selected.SupportedReasoningEfforts.Select(ToModelToken));
            throw new InvalidOperationException(
                $"Model '{selected.Id}' does not support reasoning effort '{ToModelToken(effort)}'. Available efforts: {supported}.");
        }
    }

    private static SubAgentModelCatalogItem? Find(
        SubAgentModelCatalogSnapshot snapshot,
        string? providerId,
        string? model) =>
        snapshot.Models.FirstOrDefault(candidate =>
            (providerId == null || string.Equals(candidate.ProviderId, providerId.Trim(), StringComparison.OrdinalIgnoreCase))
            && string.Equals(candidate.Model, model?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static SubAgentModelCatalogItem CreateItem(
        AppConfig config,
        string providerId,
        string model,
        ModelCatalogResult result,
        ModelCatalogEntry? entry)
    {
        var capability = entry == null
            ? null
            : ModelThinkingAdapterResolver.ResolveReasoningCapability(
                config,
                result.ProviderId,
                result.Protocol,
                result.EndPoint,
                entry);
        return new SubAgentModelCatalogItem
        {
            Id = $"{providerId}/{model}",
            ProviderId = providerId,
            Model = model,
            SupportedReasoningEfforts = capability?.SupportedEfforts
                .Select(static option => option.Effort.ToModelReasoningEffort())
                .Distinct()
                .ToList() ?? [],
            DefaultReasoningEffort = capability?.DefaultEffort.ToModelReasoningEffort()
        };
    }

    private static string RenderDescription(IReadOnlyList<SubAgentModelCatalogItem> models)
    {
        if (models.Count == 0)
        {
            return "No model overrides are currently loaded.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Available model overrides for fresh or bounded native children (optional; configured defaults are preferred):");
        foreach (var model in models)
        {
            builder.Append("- `").Append(model.Id).Append('`');
            if (model.SupportedReasoningEfforts.Count > 0)
            {
                builder.Append(". Reasoning efforts: ");
                builder.Append(string.Join(", ", model.SupportedReasoningEfforts.Select(effort =>
                    effort == model.DefaultReasoningEffort
                        ? $"{ToModelToken(effort)} (default)"
                        : ToModelToken(effort))));
            }
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd();
    }

    private static string BuildUnavailableModelMessage(SubAgentModelCatalogSnapshot snapshot)
    {
        var available = snapshot.Models.Count == 0
            ? "none"
            : string.Join(", ", snapshot.Models.Select(static model => model.Id));
        return $"The requested subagent model is not available in this thread snapshot. Available models: {available}.";
    }

    private static bool TryParseEffort(string value, out ModelReasoningEffort effort)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (string.Equals(normalized, "xhigh", StringComparison.OrdinalIgnoreCase))
        {
            effort = ModelReasoningEffort.ExtraHigh;
            return true;
        }
        return Enum.TryParse(normalized, true, out effort)
            && Enum.IsDefined(effort);
    }

    private static string ToModelToken(ModelReasoningEffort effort) => effort switch
    {
        ModelReasoningEffort.ExtraHigh => "xhigh",
        _ => effort.ToString().ToLowerInvariant()
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
