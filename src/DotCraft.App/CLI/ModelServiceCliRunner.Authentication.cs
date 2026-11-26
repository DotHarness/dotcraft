using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.CLI;

internal static partial class ModelServiceCliRunner
{
    public static async Task<int> LoginAsync(string directory, string[] plugins, string? providerId, string? method,
        bool noBrowser, TextReader input, TextWriter output, CancellationToken ct)
    {
        if (providerId is null)
        {
            if (method is not null && method != ModelProviderAuthMethods.ChatGptOAuth)
                throw new ArgumentException("The default subscription accepts only the ChatGPT authentication method.", nameof(method));
            var status = await ChatGptAuthentication(directory).LoginAsync(!noBrowser, output.WriteLine, ct);
            output.WriteLine(status.LoggedIn ? "Signed in." : "Sign-in failed.");
            return status.LoggedIn ? 0 : 1;
        }

        return await WithRuntimeAsync(directory, plugins, (config, services) =>
        {
            var provider = ModelProviderResolver.ResolveProvider(config, providerId);
            var selectedMethod = method ?? (provider.BackendId == OpenRouterBackend.Id ? OpenRouterBackend.OAuth : null);
            return RunLoginAsync(services.GetRequiredService<ProviderAuthenticationCoordinator>(),
                config, providerId, selectedMethod, noBrowser, input, output, ct);
        }, ct);
    }

    public static Task<int> StatusAsync(string directory, string[] plugins, string? providerId, TextWriter output, CancellationToken ct)
    {
        if (providerId is null)
        {
            output.WriteLine(ChatGptAuthentication(directory).GetStatus().LoggedIn ? "Signed in." : "Not signed in.");
            return Task.FromResult(0);
        }

        return WithRuntimeAsync(directory, plugins, async (config, services) =>
        {
            var provider = ModelProviderResolver.ResolveProvider(config, providerId);
            var auth = services.GetRequiredService<ProviderAuthenticationCoordinator>();
            var status = provider.BackendId is null && !provider.IsChatGptOAuth
                ? new ProviderAuthenticationStatus(!string.IsNullOrWhiteSpace(provider.ApiKey))
                : await auth.GetStatusAsync(config, providerId, ct);
            output.WriteLine(status.IsAuthenticated ? "Signed in." : "Not signed in.");
            if (status.IsAuthenticated)
            {
                ProviderUsageSnapshot? snapshot;
                try { snapshot = await auth.ReadUsageAsync(config, providerId, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    output.WriteLine("Usage unavailable.");
                    return 0;
                }
                if (snapshot is not null)
                    WriteUsage(snapshot, output);
            }
            return 0;
        }, ct);
    }

    public static async Task<int> LogoutAsync(string directory, string[] plugins, string? providerId, CancellationToken ct)
    {
        if (providerId is null)
        {
            await ChatGptAuthentication(directory).LogoutAsync(ct);
            return 0;
        }

        return await WithRuntimeAsync(directory, plugins, async (config, services) =>
        {
            await services.GetRequiredService<ProviderAuthenticationCoordinator>().LogoutAsync(config, providerId, ct);
            return 0;
        }, ct);
    }

    private static OpenAIAuthManager ChatGptAuthentication(string directory) =>
        new(new OpenAITokenStore(Path.Combine(directory, "credentials")));

    private static async Task<int> RunLoginAsync(ProviderAuthenticationCoordinator auth, AppConfig config,
        string providerId, string? method, bool noBrowser, TextReader input, TextWriter output, CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("N");
        var updates = Channel.CreateUnbounded<ProviderLoginState>();
        void Changed(string changedOwner, ProviderLoginState next)
        {
            if (owner == changedOwner)
                updates.Writer.TryWrite(next);
        }

        auth.Changed += Changed;
        try
        {
            var state = auth.Login(config, providerId, method, owner, ct);
            ProviderLoginEvent? shown = null;
            void ShowEvent(ProviderLoginState current)
            {
                if (current.Event is not { } notification || ReferenceEquals(notification, shown))
                    return;
                ShowLoginEvent(notification, noBrowser, output);
                shown = notification;
            }

            string? answered = null;
            while (true)
            {
                ShowEvent(state);
                switch (state.Status)
                {
                    case "completed":
                        output.WriteLine(state.Authentication?.IsAuthenticated == true ? "Signed in." : "Sign-in failed.");
                        return state.Authentication?.IsAuthenticated == true ? 0 : 1;
                    case "cancelled":
                        return 130;
                    case "failed":
                        output.WriteLine($"{state.ErrorCode ?? "ProviderLoginFailed"}: {state.ErrorMessage ?? "Sign-in failed."}");
                        return 1;
                }

                if (state.Prompt is { } prompt && state.PromptId is { } promptId && promptId != answered)
                {
                    answered = promptId;
                    state = await AnswerPromptAsync(auth, state, prompt, promptId, owner, input, output, updates.Reader, ShowEvent, ct);
                }
                else
                {
                    state = await ReadNewerStateAsync(updates.Reader, state, ct);
                }
            }
        }
        finally
        {
            auth.Changed -= Changed;
            auth.CancelOwner(owner);
        }
    }

    private static async Task<ProviderLoginState> AnswerPromptAsync(ProviderAuthenticationCoordinator auth,
        ProviderLoginState state, ProviderLoginPrompt prompt, string promptId, string owner,
        TextReader input, TextWriter output, ChannelReader<ProviderLoginState> updates, Action<ProviderLoginState> showEvent, CancellationToken ct)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var answer = Task.Run(() => ReadPromptAsync(prompt, input, output, reading.Token), CancellationToken.None);
        try
        {
            while (state.PromptId == promptId)
            {
                var changed = ReadNewerStateAsync(updates, state, reading.Token);
                if (await Task.WhenAny(answer, changed) == answer)
                {
                    try { auth.Respond(state.Id, promptId, await answer, owner); }
                    catch (ProviderAuthenticationException exception) when (exception.Code == "ProviderLoginPromptStale") { }
                    return await changed;
                }
                state = await changed;
                showEvent(state);
            }
            return state;
        }
        finally
        {
            await reading.CancelAsync();
        }
    }

    private static async Task<ProviderLoginState> ReadNewerStateAsync(
        ChannelReader<ProviderLoginState> updates, ProviderLoginState state, CancellationToken ct)
    {
        while (true)
        {
            var next = await updates.ReadAsync(ct);
            if (next.Id == state.Id && next.Revision > state.Revision)
                return next;
        }
    }

    private static async Task<string> ReadPromptAsync(ProviderLoginPrompt prompt, TextReader input, TextWriter output, CancellationToken ct)
    {
        if (ReferenceEquals(input, Console.In) && !Console.IsInputRedirected)
        {
            if (prompt.Kind == "select" && prompt.Choices is { Count: > 0 } selection)
                return await AnsiConsole.PromptAsync(new SelectionPrompt<string>().Title(Markup.Escape(prompt.Message))
                    .AddChoices(selection.Select(choice => choice.Id))
                    .UseConverter(id => Markup.Escape(selection.First(choice => choice.Id == id).Label)), ct);
            var text = new TextPrompt<string>(Markup.Escape(prompt.Message));
            if (prompt.Kind == "secret")
                text.Secret();
            return await AnsiConsole.PromptAsync(text, ct);
        }
        output.WriteLine(prompt.Message);
        if (prompt.Choices is { } choices)
            foreach (var choice in choices)
                output.WriteLine($"  {choice.Id}: {choice.Label}");
        return await input.ReadLineAsync(ct)
            ?? throw new ProviderAuthenticationException("ProviderLoginInputClosed", "Authorization input ended.");
    }

    private static void ShowLoginEvent(ProviderLoginEvent notification, bool noBrowser, TextWriter output)
    {
        if (notification.Message is not null)
            output.WriteLine(notification.Message);
        if (notification.UserCode is not null)
            output.WriteLine(notification.UserCode);
        if (notification.Url is { } url)
        {
            output.WriteLine(url);
            if (!noBrowser && notification.Kind is "auth_url" or "device_code"
                && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }

    private static void WriteUsage(ProviderUsageSnapshot snapshot, TextWriter output)
    {
        output.WriteLine("Usage:");
        foreach (var (key, value) in snapshot.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            output.WriteLine($"  {key}: {value.ToString(CultureInfo.InvariantCulture)}");
        foreach (var window in ProviderUsageWindowPresentation.Shape(snapshot))
            output.WriteLine($"  {window.Kind}: {window.Window.UsedPercent}% used, resets {window.Window.ResetAt:O}");
        if (snapshot.Credits is { } credits)
            output.WriteLine($"  Credits: {(credits.Unlimited ? "unlimited" : credits.Balance ?? "unavailable")}");
        if (snapshot.Balances is { } balances)
            foreach (var balance in balances)
                output.WriteLine($"  {balance.Currency}: {balance.Total}");
    }
}
