using DotCraft.Workspaces;
using System.Text.Json;
using DotCraft.Automations;
using DotCraft.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace DotCraft.Tests.Tools;
public sealed class GeneratedAutomationToolFunctionParityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automation_tools_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact]
    public async Task Automation_HasTrustedPresentationAndCannotAcceptOrigin()
    {
        var service = new AutomationService(new(), new DotCraftPaths(_root, Path.Combine(_root, ".craft"), null), NullLogger<AutomationService>.Instance);
        var registration = Assert.Single(await new AutomationToolSource(service).GetRegistrationsAsync(
            new ToolPlanningContext("thread", null, _root, Path.Combine(_root, ".craft"), "agent", null, [], 1)));
        Assert.Equal("Automation", registration.Definition.Name.Name);
        Assert.Equal("core.automation", registration.Definition.Presentation!.Id.Value);
        Assert.Equal(ToolSourceKind.CoreNative, registration.Definition.Provenance.Kind);
        Assert.DoesNotContain("deliveryTarget", registration.Definition.InputSchema.GetRawText());
        var tools = new AutomationTools(service);
        using var result = JsonDocument.Parse(await tools.Automation("create", automation: new AutomationInput {
            Name = "Check", Prompt = "Check changes", Schedule = new() { Kind = "every", EveryMs = 60000 } }));
        Assert.Equal("create", result.RootElement.GetProperty("operation").GetString());
        var id = result.RootElement.GetProperty("automation").GetProperty("id").GetString()!;
        Assert.Equal(id, Assert.Single(await service.ListAsync()).Id);
        var invalid = await Assert.ThrowsAsync<ArgumentException>(() => tools.Automation("complete", automationId: id));
        Assert.Equal("automation.invalidAction", invalid.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.Automation("report", summary: "done"));
    }
}
