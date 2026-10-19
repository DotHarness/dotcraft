using DotCraft.Agents;
using DotCraft.Configuration;
using Spectre.Console;

namespace DotCraft.CLI;

internal static class ProviderAuthCliRunner
{
    public static async Task<int> RunAsync(ProviderAuthenticationCoordinator auth, AppConfig config,
        CommandLineArgs args, CancellationToken ct)
    {
        var providerId = args.SetupProviderId ?? throw new ArgumentException("A provider identifier is required.");
        if (args.ProviderAuthAction == "logout")
        {
            await auth.LogoutAsync(config, providerId, ct);
            AnsiConsole.WriteLine("Signed out.");
            return 0;
        }
        if (args.ProviderAuthAction == "status")
        {
            var status = await auth.GetStatusAsync(config, providerId, ct);
            AnsiConsole.WriteLine(status.IsAuthenticated ? "Authenticated." : status.FailureMessage ?? "Not authenticated.");
            return status.IsAuthenticated ? 0 : 1;
        }
        var owner = Guid.NewGuid().ToString("N");
        var operation = auth.Login(config, providerId, args.ProviderAuthMethod, owner, ct);
        long revision = -1;
        string? previousPrompt = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var state = auth.Get(operation.Id, owner);
                if (state.Revision != revision)
                {
                    revision = state.Revision;
                    if (state.Event is { } notification)
                    {
                        if (notification.Message is { } message) AnsiConsole.WriteLine(message);
                        if (notification.Url is { } url) AnsiConsole.WriteLine(url);
                        if (notification.UserCode is { } code) AnsiConsole.WriteLine(code);
                    }
                    if (state.Status == "completed") { AnsiConsole.WriteLine("Authenticated."); return 0; }
                    if (state.Status is "cancelled" or "failed")
                    {
                        AnsiConsole.WriteLine(state.ErrorMessage ?? state.ErrorCode ?? "Authentication did not complete.");
                        return state.Status == "cancelled" ? 130 : 1;
                    }
                    if (state.Prompt is { } prompt && state.PromptId is { } promptId && promptId != previousPrompt)
                    {
                        previousPrompt = promptId;
                        var answer = await ReadPromptAsync(prompt, ct);
                        auth.Respond(operation.Id, promptId, answer, owner);
                    }
                }
                await Task.Delay(100, ct);
            }
        }
        finally { auth.CancelOwner(owner); }
    }

    private static Task<string> ReadPromptAsync(ProviderLoginPrompt prompt, CancellationToken ct)
    {
        if (prompt.Kind == "select" && prompt.Choices is { Count: > 0 } choices)
            return AnsiConsole.PromptAsync(new SelectionPrompt<string>()
                .Title(Markup.Escape(prompt.Message)).AddChoices(choices.Select(choice => choice.Id))
                .UseConverter(id => choices.Single(choice => choice.Id == id).Label), ct);
        var input = new TextPrompt<string>(Markup.Escape(prompt.Message));
        if (prompt.Kind == "secret") input.Secret();
        return AnsiConsole.PromptAsync(input, ct);
    }
}
