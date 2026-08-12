namespace DotCraft.Commands.Core;

/// <summary>
/// Contributes dynamically discovered slash commands that expand to model input.
/// </summary>
public interface IPromptCommandProvider
{
    IReadOnlyList<PromptCommandDefinition> ListCommands();

    string? TryResolve(string commandName, string rawArguments);
}

public sealed record PromptCommandDefinition(
    string Name,
    string Description,
    string Category = "extension");
