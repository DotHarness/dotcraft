using DotCraft.Workspaces;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Automations;
using DotCraft.Plugins;
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
        var schema = JsonNode.Parse(registration.Definition.InputSchema.GetRawText())!.AsObject();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Contains("create", schema["properties"]!["action"]!["enum"]!.AsArray().Select(static item => item!.GetValue<string>()));
        Assert.Equal(["active", "paused"], schema["properties"]!["automation"]!["properties"]!["status"]!["enum"]!.AsArray().Select(static item => item!.GetValue<string>()));
        Assert.Equal(["project", "worktree"], schema["properties"]!["automation"]!["properties"]!["workspaceMode"]!["enum"]!.AsArray().Select(static item => item!.GetValue<string>()));
        Assert.Equal(23, schema["properties"]!["automation"]!["properties"]!["schedule"]!["properties"]!["hour"]!["maximum"]!.GetValue<int>());
        Assert.Null(schema["properties"]!["automation"]!["properties"]!["schedule"]!["properties"]!["days"]!["items"]!["minItems"]);
        Assert.Equal(AutomationService.MaxMemoryChars, schema["properties"]!["memory"]!["maxLength"]!.GetValue<int>());
        var atSchema = schema["properties"]!["automation"]!["properties"]!["schedule"]!["properties"]!["at"]!;
        Assert.Equal(["string", "null"], atSchema["type"]!.AsArray().Select(static item => item!.GetValue<string>()));
        Assert.Equal("date-time", atSchema["format"]!.GetValue<string>());

        var validArguments = JsonNode.Parse("""{"action":"create","automation":{"name":"Check","prompt":"Check changes","status":"active","executionMode":"thread","targetThreadId":"","schedule":{"kind":"at","at":"2099-09-09T00:00:00Z"},"notificationPolicy":"important"}}""")!.AsObject();
        Assert.True(PluginFunctionSchemaValidator.TryValidateArguments(schema, validArguments, out var validMessage), validMessage);
        var invalidDate = JsonNode.Parse("""{"action":"create","automation":{"schedule":{"kind":"at","at":{"dateTime":"2099-09-09T00:00:00Z"}}}}""")!.AsObject();
        Assert.False(PluginFunctionSchemaValidator.TryValidateArguments(schema, invalidDate, out _));
        var invalidPolicy = JsonNode.Parse("""{"action":"create","notificationPolicy":"all","automation":{"schedule":{"kind":"every","everyMs":60000}}}""")!.AsObject();
        Assert.False(PluginFunctionSchemaValidator.TryValidateArguments(schema, invalidPolicy, out _));

        var invocation = new ToolInvocationContext(
            "thread",
            null,
            "call",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            registration.Binding.Revision,
            DateTimeOffset.UtcNow);
        var invoked = await registration.Binding.Runtime.InvokeAsync(invocation, validArguments);
        Assert.True(invoked.Success, invoked.Error?.Message);
        using var result = JsonDocument.Parse(invoked.Content!);
        Assert.Equal("create", result.RootElement.GetProperty("operation").GetString());
        var created = result.RootElement.GetProperty("automation");
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("thread", created.GetProperty("targetThreadId").GetString());
        Assert.Equal("project", created.GetProperty("workspaceMode").GetString());
        Assert.Equal("workspaceScope", created.GetProperty("approvalPolicy").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("agentProfileId").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("schedule").GetProperty("timeZone").ValueKind);
        Assert.Equal(id, Assert.Single(await service.ListAsync()).Id);

        var unsupportedApproval = JsonNode.Parse("""{"action":"create","automation":{"name":"Check","prompt":"Check changes","status":"active","executionMode":"independent","approvalPolicy":"auto","schedule":{"kind":"at","at":"2099-09-09T00:00:00Z"},"notificationPolicy":"all"}}""")!.AsObject();
        var rejected = await registration.Binding.Runtime.InvokeAsync(invocation, unsupportedApproval);
        Assert.False(rejected.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, rejected.Error?.Code);

        var invalidAction = JsonNode.Parse("""{"action":"complete"}""")!.AsObject();
        Assert.False((await registration.Binding.Runtime.InvokeAsync(invocation, invalidAction)).Success);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.Automation(AutomationToolAction.Report, summary: "done"));
    }
}
