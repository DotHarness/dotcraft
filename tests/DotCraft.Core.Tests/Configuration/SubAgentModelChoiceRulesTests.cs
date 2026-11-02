using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class SubAgentModelChoiceRulesTests
{
    [Fact]
    public void Validate_RequiresAKnownProviderOnlyForAddedEntries()
    {
        SubAgentModelChoice[] previous = [Choice("removed", "kept-model")];
        Func<string, bool> exists = providerId => providerId == "openai";

        Assert.Null(SubAgentModelChoiceRules.Validate(previous, [Choice("removed", "kept-model"), Choice("openai", "gpt")], exists));
        Assert.NotNull(SubAgentModelChoiceRules.Validate(previous, [Choice("missing", "model")], exists));
        Assert.NotNull(SubAgentModelChoiceRules.Validate([], [Choice("openai", "gpt"), Choice("OPENAI", "GPT")], exists));
        Assert.NotNull(SubAgentModelChoiceRules.Validate(
            [],
            [.. Enumerable.Range(0, SubAgentModelChoiceRules.MaxChoices + 1).Select(index => Choice("openai", $"model-{index}"))],
            exists));
    }

    private static SubAgentModelChoice Choice(string providerId, string model) => new() { ProviderId = providerId, Model = model };
}
