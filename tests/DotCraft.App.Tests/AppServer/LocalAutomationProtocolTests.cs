using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Channels;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class LocalAutomationProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automation-wire-" + Guid.NewGuid().ToString("N"));
    private readonly AutomationService _service;
    private readonly AutomationsRequestHandler _handler;

    public LocalAutomationProtocolTests()
    {
        Directory.CreateDirectory(_root);
        _service = new(new AutomationsConfig(), new DotCraftPaths(_root, Path.Combine(_root, ".craft"), null), NullLogger<AutomationService>.Instance);
        _handler = new(_service);
    }

    [Fact]
    public async Task CreateReadUpdate_PreserveFieldsAndRejectStaleWrites()
    {
        var input = Input();
        var created = await _handler.HandleCreateAsync(new() { Automation = input }, default);
        var id = created.Automation.Id;
        Assert.Equal("weekly", created.Automation.Schedule.Kind);
        Assert.Equal(new[] { 5 }, created.Automation.Schedule.Days);
        Assert.Equal("UTC", created.Automation.Schedule.TimeZone);
        Assert.Equal("all", created.Automation.NotificationPolicy);
        var edited = await _handler.HandleUpdateAsync(new()
        {
            AutomationId = id, ExpectedVersion = 1,
            Automation = Input("Edited", "paused")
        }, default);
        Assert.Equal(2, edited.Automation.Version);
        Assert.Null(edited.Automation.NextRunAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleUpdateAsync(new()
        {
            AutomationId = id, ExpectedVersion = 1, Automation = input
        }, default));
        var current = await _handler.HandleReadAsync(new() { AutomationId = id }, default);
        Assert.Equal("Edited", current.Automation.Name);
        Assert.Single((await _handler.HandleListAsync(new(), default)).Automations);
    }

    [Fact]
    public async Task Create_CapturesGroupTargetWithoutConfusingExecutionTarget()
    {
        using var scope = ChannelSessionScope.Set(new()
        {
            Channel = "telegram", UserId = "user-1", GroupId = "group-2", DefaultDeliveryTarget = "chat:-42"
        });
        var created = await _handler.HandleCreateAsync(new() { Automation = Input() }, default);
        Assert.Equal("chat:-42", created.Automation.Origin!.DeliveryTarget);
        Assert.Equal("group-2", created.Automation.Origin.GroupId);
        Assert.Equal("independent", created.Automation.ExecutionMode);
        Assert.Null(created.Automation.TargetThreadId);
    }

    [Fact]
    public async Task Storage_DoesNotReadOrRemoveOtherCraftData()
    {
        var otherData = Path.Combine(_root, ".craft", "tasks", "example");
        Directory.CreateDirectory(otherData);
        var existingFile = Path.Combine(otherData, "task.md");
        await File.WriteAllTextAsync(existingFile, "status: pending");
        Assert.Empty((await _handler.HandleListAsync(new(), default)).Automations);
        var created = await _handler.HandleCreateAsync(new() { Automation = Input() }, default);
        await _handler.HandleDeleteAsync(new() { AutomationId = created.Automation.Id }, default);
        Assert.Empty((await _handler.HandleListAsync(new(), default)).Automations);
        Assert.True(File.Exists(existingFile));
    }

    [Fact]
    public void RunMapping_PreservesSpecificTurnAndDeliveryFailure()
    {
        var run = new AutomationRun
        {
            Id = "run-2", AutomationId = "auto-1", ThreadId = "thread-7", TurnId = "turn-9",
            Status = "succeeded", Summary = "Done", DeliveryStatus = "failed", DeliveryError = "offline"
        };
        var wire = AutomationsRequestHandler.ToWire(run);
        Assert.Equal("turn-9", wire.TurnId);
        Assert.Equal("run-2", wire.Id);
        Assert.Equal("succeeded", wire.Status);
        Assert.Equal("failed", wire.DeliveryStatus);
    }

    [Fact]
    public async Task Presets_ProvideConversationStartersWithRealSchedules()
    {
        var result = await _handler.HandlePresetsListAsync(new(), default);

        Assert.Equal(4, result.Presets.Count);
        Assert.All(result.Presets, preset => Assert.NotNull(preset.Schedule));
        Assert.Equal("weekly", result.Presets.Single(preset => preset.Id == "weekly-review").Schedule!.Kind);
        Assert.Equal(3_600_000, result.Presets.Single(preset => preset.Id == "ci-monitor").Schedule!.EveryMs);
    }

    private static Contract.AutomationInput Input(string name = "Weekly report", string status = "active") => new()
    {
        Name = name, Prompt = "Summarize changes", Status = status, WorkspaceMode = "project",
        Schedule = new() { Kind = "weekly", Days = [5], Hour = 9, Minute = 0, TimeZone = "UTC" }
    };

    public void Dispose() => Directory.Delete(_root, true);
}
