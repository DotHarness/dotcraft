using System.Text.Json;
using DotCraft.Commands.Core;
namespace DotCraft.Automations;
/// <summary>Deterministic channel commands backed by the unified service.</summary>
public sealed class AutomationCommandHandler(AutomationService service) : ICommandHandler
{
    public string[] Commands => ["/automate"];
    public CommandRegistration Metadata => new()
    {
        Name = "/automate",
        DescriptionKey = "command.automate.description",
        FallbackDescription = "Manage scheduled automations"
    };
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        var action = context.Arguments.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        var id = context.Arguments.ElementAtOrDefault(1);
        try
        {
            if (action == "list")
            {
                var definitions = await service.ListAsync();
                return CommandResult.HandledResult(definitions.Count == 0 ? "No automations." : string.Join("\n", definitions.Select(d => $"{d.Id} · {d.Name} · {d.Status}")));
            }
            if (id == null) return CommandResult.HandledResult("Usage: /automate list|show|pause|resume|run|remove [id]");
            if (action == "remove") { await service.DeleteAsync(id); return CommandResult.HandledResult("Automation removed."); }
            if (action == "run") { var run = await service.RunAsync(id); return CommandResult.HandledResult($"Automation queued: {run.Id}"); }
            var definition = await service.ReadAsync(id);
            if (action == "show") return CommandResult.HandledResult(JsonSerializer.Serialize(definition, AutomationStore.Json));
            if (action is not ("pause" or "resume")) return CommandResult.HandledResult("Usage: /automate list|show|pause|resume|run|remove [id]");
            var updated = await service.UpdateAsync(id, definition.Version, definition with { Status = action == "pause" ? "paused" : "active" });
            return CommandResult.HandledResult($"{updated.Name}: {updated.Status}");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException) { return CommandResult.HandledResult(ex.Message); }
    }
}
