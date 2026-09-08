using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Channels;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Protocol;

/// <summary>Projects the workspace automation service through the typed AppServer boundary.</summary>
public sealed class AutomationsRequestHandler(AutomationService service) : IAutomationsRequestHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Contract.AutomationListResult> HandleListAsync(DotCraft.Protocol.RpcEmpty parameters, CancellationToken ct) =>
        new() { Automations = (await service.ListAsync(ct)).Select(ToWire).ToList() };

    public async Task<Contract.AutomationReadResult> HandleReadAsync(Contract.AutomationIdParams parameters, CancellationToken ct) =>
        new() { Automation = ToWire(await service.ReadAsync(parameters.AutomationId, ct)) };

    public async Task<Contract.AutomationReadResult> HandleCreateAsync(Contract.AutomationCreateParams parameters, CancellationToken ct)
    {
        var source = ChannelSessionScope.Current;
        var origin = source == null ? null : new AutomationOrigin
        {
            Channel = source.Channel, UserId = source.UserId, GroupId = source.GroupId,
            DeliveryTarget = source.DefaultDeliveryTarget
        };
        return new() { Automation = ToWire(await service.CreateAsync(Map<AutomationInput>(parameters.Automation), origin, ct)) };
    }

    public async Task<Contract.AutomationReadResult> HandleUpdateAsync(Contract.AutomationUpdateParams parameters, CancellationToken ct) =>
        new() { Automation = ToWire(await service.UpdateAsync(parameters.AutomationId, parameters.ExpectedVersion, Map<AutomationInput>(parameters.Automation), ct)) };

    public async Task<Contract.AutomationDeleteResult> HandleDeleteAsync(Contract.AutomationIdParams parameters, CancellationToken ct)
    {
        await service.DeleteAsync(parameters.AutomationId, ct);
        return new() { Ok = true };
    }

    public async Task<Contract.AutomationRunResult> HandleRunAsync(Contract.AutomationIdParams parameters, CancellationToken ct) =>
        new() { Run = ToWire(await service.RunAsync(parameters.AutomationId, ct)) };

    public async Task<Contract.AutomationRunsResult> HandleRunsListAsync(Contract.AutomationIdParams parameters, CancellationToken ct) =>
        new() { Runs = (await service.ListRunsAsync(parameters.AutomationId, ct)).Select(ToWire).ToList() };

    public Task<Contract.AutomationPresetsResult> HandlePresetsListAsync(Contract.AutomationPresetsParams parameters, CancellationToken ct) =>
        Task.FromResult(new Contract.AutomationPresetsResult { Presets = service.Presets().Select(Map<Contract.AutomationPreset>).ToList() });

    public static Contract.AutomationDefinition ToWire(AutomationDefinition definition) => Map<Contract.AutomationDefinition>(definition);
    public static Contract.AutomationRun ToWire(AutomationRun run) => Map<Contract.AutomationRun>(run);

    private static T Map<T>(object value)
    {
        if (value == null) throw new ArgumentException("automation.definitionRequired");
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)
            ?? throw new InvalidOperationException("Invalid automation contract mapping.");
    }
}
