using DotCraft.Commands.Core;
using Xunit;

namespace DotCraft.Tests.Commands;

public sealed class CommandDescriptionTests
{
    [Theory]
    [InlineData("cmd.new", "Module fallback", "Create a new session")]
    [InlineData("module.command.description", "Module fallback", "Module fallback")]
    [InlineData("", "Module fallback", "Module fallback")]
    [InlineData("module.command.description", null, "/module-command")]
    public void Discovery_UsesKnownKeyThenModuleFallbackThenName(string key, string? fallback, string expected)
    {
        var registry = CommandRegistry.CreateDefault(".craft");
        registry.RegisterHandler(new ModuleCommand(), new CommandRegistration
        {
            Name = "/module-command", DescriptionKey = key, FallbackDescription = fallback
        });

        var command = registry.ListCommands().Single(item => item.Name == "/module-command");
        Assert.Equal(key, command.DescriptionKey);
        Assert.Equal(expected, command.Description);
        Assert.Equal(expected, command.FallbackDescription);
    }

    private sealed class ModuleCommand : ICommandHandler
    {
        public string[] Commands => ["/module-command"];
        public Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder) =>
            Task.FromResult(CommandResult.HandledResult());
    }
}
