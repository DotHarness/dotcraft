using System.Text;
using DotCraft.Oratorio.Data;

namespace DotCraft.Oratorio.Services;

internal sealed record DiscussionQuestionInput(string Text, string? InlineSection);

internal static class DiscussionTurnPrompt
{
    public static string Build(OratorioDiscussionTurn turn, DiscussionQuestionInput question, string? stateSection, bool runActive, string? reviewTarget)
    {
        var prompt = new StringBuilder();
        if (reviewTarget is not null)
        {
            prompt.Append(reviewTarget);
            prompt.AppendLine();
        }

        if (stateSection is not null)
        {
            prompt.Append(stateSection);
            prompt.AppendLine();
        }

        prompt.AppendLine($"Agent Discussion Turn: discussionTurnId `{turn.DiscussionTurnId}`");
        prompt.AppendLine(turn.OriginProvider == DiscussionTurnService.GitHubOrigin
            ? $"Asked by @{turn.OriginAuthorLogin} on GitHub: {turn.OriginUrl ?? "no link"}"
            : "Asked by the operator in Oratorio.");
        if (question.InlineSection is not null)
        {
            prompt.Append(question.InlineSection);
        }

        prompt.AppendLine();
        prompt.AppendLine("Question:");
        prompt.AppendLine(question.Text);
        prompt.AppendLine();
        prompt.AppendLine(runActive
            ? "Answer this question with oratorio_run.SubmitDiscussionReply, then continue the current task."
            : "Answer only this question with oratorio_run.SubmitDiscussionReply. Submit no review draft.");
        return prompt.ToString();
    }
}
