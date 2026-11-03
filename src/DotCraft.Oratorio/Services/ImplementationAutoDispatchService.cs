using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Services;

public sealed class ImplementationAutoDispatchService(
    OratorioDbContext db,
    OratorioService oratorio,
    IDotCraftWorkspaceResolver workspaceResolver,
    IOptionsMonitor<OratorioAutomationOptions> options,
    ILogger<ImplementationAutoDispatchService> logger)
{
    public async Task DispatchEligibleAsync(CancellationToken ct)
    {
        var policy = options.CurrentValue;
        if (!policy.AutoDispatchEnabled)
        {
            return;
        }

        var candidates = await db.Items.AsNoTracking()
            .Where(x => x.State == ItemState.Discovered && (x.Kind == ItemKind.LocalTask || (x.Source == "github" || x.Source == "gitlab") && x.Kind == ItemKind.Issue))
            .OrderBy(x => x.UpdatedAt)
            .Select(x => new { x.Source, x.ExternalId, x.LabelsJson, x.Repository })
            .ToListAsync(ct);

        foreach (var item in candidates.Where(x => IsEligible(x.LabelsJson, policy) && HasWorkspace(x.Repository)).Take(20))
        {
            try
            {
                await oratorio.DispatchAsync(
                    item.Source,
                    item.ExternalId,
                    new DispatchRequest(
                        "appServer",
                        "Oratorio auto-dispatched an eligible implementation item.",
                        null,
                        null,
                        "implementation",
                        policy.DeliveryPolicy),
                    RunDispatchTrigger.AutoImplementation,
                    ct);
            }
            catch (OratorioApiException ex) when (ex.Code is "activeRunExists" or "invalidTransition")
            {
                logger.LogDebug("Skipped auto-dispatch for {Source} {ExternalId}: {Code}.", item.Source, item.ExternalId, ex.Code);
            }
        }
    }

    private bool HasWorkspace(string? repository)
    {
        try
        {
            _ = workspaceResolver.ResolveWorkspacePath(repository);
            return true;
        }
        catch (DotCraftWorkspaceResolutionException)
        {
            return false;
        }
    }

    private static bool IsEligible(string? labelsJson, OratorioAutomationOptions policy)
    {
        var labels = ParseLabels(labelsJson);
        if (policy.AutoDispatchBlockLabels.Any(blocked => labels.Contains(blocked, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return policy.AutoDispatchAllowLabels.Length == 0 ||
            policy.AutoDispatchAllowLabels.Any(allowed => labels.Contains(allowed, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseLabels(string? labelsJson)
    {
        if (string.IsNullOrWhiteSpace(labelsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(labelsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
