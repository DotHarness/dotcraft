using System.Text.Json;
using DotCraft.Core.Tests.Tools;
using DotCraft.Tools;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RequestUserInputToolsTests
{
    [Fact]
    public async Task RequestUserInput_WithoutTurnServices_ReturnsError()
    {
        var tool = new RequestUserInputTools();

        var result = await tool.RequestUserInput([ValidQuestion()], TurnToolInvocation.For(null));

        using var doc = JsonDocument.Parse(result);
        var error = doc.RootElement.GetProperty("error").GetString();
        Assert.Contains("root Session Core turn", error);
        Assert.DoesNotContain("plan", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestUserInput_RejectsInvalidQuestions()
    {
        var tool = new RequestUserInputTools();
        var invocation = Invocation(_ => Task.FromResult(new RequestUserInputResponse()));

        var question = ValidQuestion();
        question.Options = [new RequestUserInputQuestionOption { Label = "Only one" }];

        var result = await tool.RequestUserInput([question], invocation);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("2 to 3 options", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RequestUserInput_FromTopLevelThread_ReturnsAnswers()
    {
        var tool = new RequestUserInputTools();
        IReadOnlyList<RequestUserInputQuestion>? captured = null;
        var invocation = Invocation(questions =>
        {
            captured = questions;
            return Task.FromResult(new RequestUserInputResponse
            {
                Answers = new Dictionary<string, RequestUserInputAnswer>(StringComparer.Ordinal)
                {
                    ["provider_id_handling"] = new RequestUserInputAnswer
                    {
                        Answers = ["Auto-generate (Recommended)"]
                    }
                }
            });
        });

        var result = await tool.RequestUserInput([ValidQuestion()], invocation);

        Assert.NotNull(captured);
        Assert.Equal("provider_id_handling", Assert.Single(captured!).Id);
        using var doc = JsonDocument.Parse(result);
        var answer = doc.RootElement
            .GetProperty("answers")
            .GetProperty("provider_id_handling")
            .GetProperty("answers")[0]
            .GetString();
        Assert.Equal("Auto-generate (Recommended)", answer);
    }

    private static ToolInvocationContext Invocation(
        Func<IReadOnlyList<RequestUserInputQuestion>, Task<RequestUserInputResponse>> request) =>
        TurnToolInvocation.For(new TurnToolServices
        {
            IsTopLevelThread = true,
            RequestUserInput = new RequestUserInputRuntimeContext(request)
        });

    private static RequestUserInputQuestion ValidQuestion() => new()
    {
        Id = "provider_id_handling",
        Header = "Provider ID",
        Question = "When creating a provider, should users handle the provider id directly?",
        Options =
        [
            new RequestUserInputQuestionOption
            {
                Label = "Auto-generate (Recommended)",
                Description = "DotCraft creates a stable id from the provider name."
            },
            new RequestUserInputQuestionOption
            {
                Label = "Required",
                Description = "Users must type the id explicitly."
            }
        ]
    };
}
