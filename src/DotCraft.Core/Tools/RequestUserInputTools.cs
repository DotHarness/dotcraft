using System.ComponentModel;
using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.Tools;

/// <summary>
/// Tool that asks the user short structured questions and waits for answers.
/// </summary>
public sealed class RequestUserInputTools
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [Tool(Icon = "❓", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.RequestUserInput))]
    [Description("Request user input for one to three short questions and wait for the response. This tool is available in root user threads in Agent and Plan modes. Each question must have 2-3 options; put the recommended option first and suffix its label with '(Recommended)'. Do not include an Other option because the client adds free-form input automatically.")]
    public async Task<string> RequestUserInput(
        [Description("Questions to show the user. Prefer 1 and do not exceed 3. Each question has id, header, question, and 2-3 selectable options with label and description.")]
        List<RequestUserInputQuestion> questions)
    {
        var context = RequestUserInputRuntimeScope.Current;
        if (context is null)
            return Serialize(new { error = "RequestUserInput is only available inside a root Session Core turn." });

        var validationError = Validate(questions);
        if (validationError != null)
            return Serialize(new { error = validationError });

        var response = await context.RequestAsync(questions);
        return Serialize(response);
    }

    private static string? Validate(IReadOnlyList<RequestUserInputQuestion>? questions)
    {
        if (questions is not { Count: >= 1 and <= 3 })
            return "RequestUserInput.questions must contain 1 to 3 questions.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
                return "Each question must have a non-empty id.";
            if (!ids.Add(question.Id.Trim()))
                return $"Duplicate question id: {question.Id}";
            if (string.IsNullOrWhiteSpace(question.Header))
                return $"Question '{question.Id}' must have a non-empty header.";
            if (string.IsNullOrWhiteSpace(question.Question))
                return $"Question '{question.Id}' must have a non-empty question.";
            if (question.Options is not { Count: >= 2 and <= 3 })
                return $"Question '{question.Id}' must have 2 to 3 options.";
            if (question.Options.Any(option => string.IsNullOrWhiteSpace(option.Label)))
                return $"Question '{question.Id}' has an option with an empty label.";
        }

        return null;
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
