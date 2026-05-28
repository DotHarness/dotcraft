using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class RequestUserInputToolsTests
{
    [Fact]
    public async Task RequestUserInput_WithoutRuntimeScope_ReturnsError()
    {
        var tool = new RequestUserInputTools();

        var result = await tool.RequestUserInput([ValidQuestion()]);

        using var doc = JsonDocument.Parse(result);
        var error = doc.RootElement.GetProperty("error").GetString();
        Assert.Contains("root Session Core turn", error);
        Assert.DoesNotContain("plan", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestUserInput_RejectsInvalidQuestions()
    {
        var tool = new RequestUserInputTools();
        using var scope = RequestUserInputRuntimeScope.Set(new RequestUserInputRuntimeContext(
            _ => Task.FromResult(new RequestUserInputResponse())));

        var question = ValidQuestion();
        question.Options = [new RequestUserInputQuestionOption { Label = "Only one" }];

        var result = await tool.RequestUserInput([question]);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("2 to 3 options", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RequestUserInput_WithRuntimeScope_ReturnsAnswers()
    {
        var tool = new RequestUserInputTools();
        IReadOnlyList<RequestUserInputQuestion>? captured = null;
        using var scope = RequestUserInputRuntimeScope.Set(new RequestUserInputRuntimeContext(questions =>
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
        }));

        var result = await tool.RequestUserInput([ValidQuestion()]);

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

    [Fact]
    public void RequestUserInput_DisplayFormatter_FormatsQuestionCount()
    {
        Assert.Equal(
            "Ask 1 question",
            CoreToolDisplays.RequestUserInput(new Dictionary<string, object?>
            {
                ["questions"] = new[] { new object() }
            }));
        Assert.Equal(
            "Ask 3 questions",
            CoreToolDisplays.RequestUserInput(new Dictionary<string, object?>
            {
                ["questions"] = new JsonArray(new JsonObject(), new JsonObject(), new JsonObject())
            }));
        Assert.Equal(
            "Ask questions",
            CoreToolDisplays.RequestUserInput(new Dictionary<string, object?>()));
    }

    [Fact]
    public void RequestUserInput_ToolMetadata_RegistersDisplayFormatter()
    {
        var method = typeof(RequestUserInputTools).GetMethod(nameof(RequestUserInputTools.RequestUserInput));

        var attr = method?.GetCustomAttribute<ToolAttribute>();

        Assert.NotNull(attr);
        Assert.Equal(typeof(CoreToolDisplays), attr!.DisplayType);
        Assert.Equal(nameof(CoreToolDisplays.RequestUserInput), attr.DisplayMethod);
    }

    [Fact]
    public void RequestUserInput_Descriptions_MatchQuestionGuidance()
    {
        var method = typeof(RequestUserInputTools).GetMethod(nameof(RequestUserInputTools.RequestUserInput));
        var methodDescription = method?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var questionsDescription = method?.GetParameters()
            .Single(parameter => parameter.Name == "questions")
            .GetCustomAttribute<DescriptionAttribute>()
            ?.Description;

        Assert.Contains("one to three short questions", methodDescription);
        Assert.Contains("Agent and Plan modes", methodDescription);
        Assert.Contains("recommended option first", methodDescription);
        Assert.Contains("Do not include an Other option", methodDescription);
        Assert.Contains("Questions to show the user", questionsDescription);
        Assert.Contains("Prefer 1 and do not exceed 3", questionsDescription);
    }

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
