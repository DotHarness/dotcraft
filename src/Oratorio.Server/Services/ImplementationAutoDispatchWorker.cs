using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;

namespace Oratorio.Server.Services;

public sealed class ImplementationAutoDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OratorioAutomationOptions> options,
    ILogger<ImplementationAutoDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!options.CurrentValue.AutoDispatchEnabled)
            {
                continue;
            }

            try
            {
                await DispatchEligibleItemsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Implementation auto-dispatch tick failed.");
            }
        }
    }

    private async Task DispatchEligibleItemsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var candidates = await db.Items.AsNoTracking()
            .Where(x => x.State == ItemState.Discovered && (x.Kind == ItemKind.LocalTask || (x.Source == "github" || x.Source == "gitlab") && x.Kind == ItemKind.Issue))
            .OrderBy(x => x.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);
        var service = scope.ServiceProvider.GetRequiredService<OratorioService>();

        foreach (var item in candidates)
        {
            if (!IsEligible(item, options.CurrentValue))
            {
                continue;
            }

            try
            {
                await service.DispatchAsync(
                    item.Source,
                    item.ExternalId,
                    new DispatchRequest(
                        "appServer",
                        "Oratorio auto-dispatched an eligible implementation item.",
                        null,
                        null,
                        "implementation",
                        options.CurrentValue.DeliveryPolicy),
                    RunDispatchTrigger.AutoImplementation,
                    ct);
            }
            catch (OratorioApiException ex) when (ex.Code is "activeRunExists" or "invalidTransition")
            {
                logger.LogDebug("Skipped auto-dispatch for {Source} {ExternalId}: {Code}.", item.Source, item.ExternalId, ex.Code);
            }
        }
    }

    private static bool IsEligible(OratorioItem item, OratorioAutomationOptions policy)
    {
        var labels = ParseLabels(item.LabelsJson);
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
