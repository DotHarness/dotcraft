using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

public sealed class ModelCommandHandler : ICommandHandler
{
    private const int ChoiceLimit = 25;

    public string[] Commands => ["/model"];

    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context is not { SessionService: { } sessions, Config: { } config, ModelProviders: { } providers })
            return CommandResult.HandledResult(FallbackText.CommandServiceUnavailable);

        var thread = await sessions.GetThreadAsync(context.SessionId);
        var catalog = await ModelProviderCatalog.FetchAsync(config, providers, thread.Configuration?.ProviderId);
        var requested = string.Join(' ', context.Arguments).Trim();
        if (requested.Length == 0)
        {
            var current = CurrentModel(config, thread) ?? FallbackText.CommandStatusUnknown;
            if (!catalog.Success)
                return CommandResult.HandledResult($"{FallbackText.CommandModelCurrent(current)}\n{FallbackText.CommandModelUnavailable(catalog.ErrorMessage ?? string.Empty)}");

            var choices = catalog.Models
                .Take(ChoiceLimit)
                .Select(static model => new CommandChoice(model.Id, $"/model {model.Id}"))
                .ToArray();
            return CommandResult.HandledResult(FallbackText.CommandModelCurrent(current), choices: choices);
        }

        var model = requested;
        if (catalog.Success)
        {
            var listed = catalog.Models.FirstOrDefault(entry => string.Equals(entry.Id, requested, StringComparison.OrdinalIgnoreCase));
            if (listed == null)
                return CommandResult.HandledResult(FallbackText.CommandModelUnknown(requested));
            model = listed.Id;
        }

        var configuration = thread.Configuration is null ? new ThreadConfiguration() : ThreadConfigurationCloner.Clone(thread.Configuration);
        if (!string.Equals(configuration.Model, model, StringComparison.Ordinal))
            configuration.Reasoning = null;
        configuration.Model = model;
        await sessions.UpdateThreadConfigurationAsync(thread.Id, configuration);
        return CommandResult.HandledResult(FallbackText.CommandModelSet(model));
    }

    internal static string? CurrentModel(AppConfig? config, SessionThread thread)
    {
        if (!string.IsNullOrWhiteSpace(thread.Configuration?.Model))
            return thread.Configuration.Model;
        if (config == null)
            return null;
        try
        {
            return ModelProviderResolver.ResolveMain(config, thread.Configuration?.ProviderId).Model;
        }
        catch (Exception ex) when (ex is ArgumentException or ModelProviderConfigurationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
