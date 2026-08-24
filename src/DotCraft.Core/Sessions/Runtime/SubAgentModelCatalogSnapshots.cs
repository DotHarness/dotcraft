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
        ModelCatalogResult result;
        try
        {
            result = await ModelProviderCatalog.FetchAsync(
                config,
                providerRegistry,
                providerId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = new ModelCatalogResult { Success = false };
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = result.Success
            ? result.Models
                .Select(static model => model.Id.Trim())
                .Where(model => model.Length > 0 && seen.Add(model))
                .Take(MaxModelOverrides)
                .Select(model => CreateItem(config, result.Protocol, result.EndPoint, model))
                .ToList()
            : [];

        return new SubAgentModelCatalogSnapshot
        {
            ProviderId = providerId,
            Models = items,
            Description = RenderDescription(items)
        };
    }

    public static SubAgentModelCatalogSnapshot Clone(SubAgentModelCatalogSnapshot source) => new()
    {
        ProviderId = source.ProviderId,
        Description = source.Description,
        Models = source.Models.Select(static model => new SubAgentModelCatalogItem
        {
            Id = model.Id,
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
            selected = snapshot.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, inheritedModel.Trim(), StringComparison.OrdinalIgnoreCase));
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
            Model = normalizedModel == null ? null : selected?.Id,
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
        var selected = snapshot.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, model, StringComparison.OrdinalIgnoreCase));
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

    private static SubAgentModelCatalogItem CreateItem(
        AppConfig config,
        string? protocol,
        string? endpoint,
        string model)
    {
        var capability = ModelThinkingAdapterResolver.ResolveReasoningCapability(
            config,
            protocol,
            endpoint,
            model);
        return new SubAgentModelCatalogItem
        {
            Id = model,
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
