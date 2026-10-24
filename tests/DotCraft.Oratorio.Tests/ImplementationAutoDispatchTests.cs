using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class ImplementationAutoDispatchTests
{
    [Fact]
    public async Task AutoDispatch_ReachesAllowedItemBehindManyUnlabelledItems()
    {
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(new OratorioAutomationOptions
        {
            AutoDispatchEnabled = true,
            AutoDispatchAllowLabels = ["oratorio:auto"]
        });
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(new FakeAppServerClientFactory(FakeAppServerOutcome.Hold));
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });

        string allowedItemId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            for (var index = 1; index <= 25; index++)
            {
                db.Items.Add(LocalTask($"task:unlabelled-{index}", null, createdAt.AddMinutes(index)));
            }

            var allowed = LocalTask("task:allowed", "[\"oratorio:auto\"]", createdAt.AddDays(1));
            db.Items.Add(allowed);
            allowedItemId = allowed.ItemId;
            await db.SaveChangesAsync();
        }

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ImplementationAutoDispatchService>().DispatchEligibleAsync(CancellationToken.None);
        }

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var runs = await verifyDb.Runs.AsNoTracking().ToListAsync();
        var run = Assert.Single(runs);
        Assert.Equal(allowedItemId, run.ItemId);
        Assert.Equal(RunDispatchTrigger.AutoImplementation, run.DispatchTrigger);
    }

    private static OratorioItem LocalTask(string externalId, string? labelsJson, DateTimeOffset updatedAt) =>
        new()
        {
            Source = "local",
            ExternalId = externalId,
            Kind = ItemKind.LocalTask,
            Title = externalId,
            Repository = "example-owner/oratorio",
            LabelsJson = labelsJson,
            SourceState = SourceState.Open,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
}
