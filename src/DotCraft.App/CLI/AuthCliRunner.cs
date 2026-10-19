using System.Diagnostics;
using System.Threading.Channels;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Hub;
using DotCraft.Text;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.CLI;

internal static class AuthCliRunner
{
    public static Task<int> RunStandaloneAsync(CommandLineArgs args, CancellationToken ct) =>
        RunStandaloneAsync(args, HubPaths.ForCurrentUser(), Console.In, ct);

    internal static async Task<int> RunStandaloneAsync(
        CommandLineArgs args, HubPaths paths, TextReader input, CancellationToken ct)
    {
        var config = AppConfig.Load(paths.GlobalConfigPath);
        if (args.AuthReadStdin)
            args = args with { SetupProviderId = ModelCatalogCliRunner.ApplyProviderDraft(config, await input.ReadToEndAsync(ct)) };
        await using var services = new ServiceCollection()
            .AddOpenAIModelProvider(paths.CraftHomePath)
            .AddAnthropicModelProvider()
            .BuildServiceProvider();
        using var resolver = new ModelBackendResolver(
            new ModelProviderRegistry(services.GetServices<IModelProvider>()), new NoContributions(), paths.CraftHomePath);
        await using var coordinator = new ProviderAuthenticationCoordinator(resolver);
        return await RunAsync(coordinator, resolver, config, args, ct);
    }

    public static async Task<int> RunAsync(ProviderAuthenticationCoordinator auth, IModelRuntimeResolver resolver,
        AppConfig config, CommandLineArgs args, CancellationToken ct)
    {
        try
        {
            if (args.AuthAction == "verify")
                return await VerifyAsync(auth, resolver, config, args.SetupProviderId ?? config.ProviderId, ct);
            var providerId = args.SetupProviderId ?? throw new ArgumentException("A provider identifier is required.");
            return args.AuthAction switch
            {
                "login" => await LoginAsync(auth, config, providerId, args, ct),
                "status" => await StatusAsync(auth, config, providerId, ct),
                "logout" => await LogoutAsync(auth, config, providerId, ct),
                _ => throw new ArgumentException($"Unknown auth action '{args.AuthAction}'.")
            };
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[yellow]{FallbackText.AuthLoginCancelled.EscapeMarkup()}[/]");
            return 130;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    private static async Task<int> LoginAsync(ProviderAuthenticationCoordinator auth, AppConfig config,
        string providerId, CommandLineArgs args, CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("N");
        var states = Channel.CreateUnbounded<ProviderLoginState>();
        void OnChanged(string changedOwner, ProviderLoginState state)
        {
            if (changedOwner == owner) states.Writer.TryWrite(state);
        }

        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthLoginStarting(providerId).EscapeMarkup()}[/]");
        auth.Changed += OnChanged;
        try
        {
            var state = auth.Login(config, providerId, args.AuthMethod, owner, ct);
            ProviderLoginEvent? shown = null;
            string? answered = null;
            while (true)
            {
                if (state.Event is { } notification && !ReferenceEquals(notification, shown))
                {
                    shown = notification;
                    ShowEvent(notification, args.AuthNoBrowser);
                }
                switch (state.Status)
                {
                    case "completed":
                        var status = state.Authentication ?? new ProviderAuthenticationStatus(true);
                        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthStatusSignedIn(Account(status), Plan(status)).EscapeMarkup()}[/]");
                        return 0;
                    case "cancelled":
                        AnsiConsole.MarkupLine($"[yellow]{FallbackText.AuthLoginCancelled.EscapeMarkup()}[/]");
                        return 130;
                    case "failed":
                        AnsiConsole.MarkupLine($"[red]{FallbackText.AuthLoginFailed(state.ErrorMessage ?? state.ErrorCode ?? string.Empty).EscapeMarkup()}[/]");
                        return 1;
                }
                if (state.Prompt is { } prompt && state.PromptId is { } promptId && promptId != answered)
                {
                    answered = promptId;
                    auth.Respond(state.Id, promptId, await ReadPromptAsync(prompt, ct), owner);
                }
                var next = await states.Reader.ReadAsync(ct);
                if (next.Id == state.Id && next.Revision > state.Revision)
                    state = next;
            }
        }
        finally
        {
            auth.Changed -= OnChanged;
            auth.CancelOwner(owner);
        }
    }

    private static async Task<int> StatusAsync(ProviderAuthenticationCoordinator auth, AppConfig config,
        string providerId, CancellationToken ct)
    {
        var status = await auth.GetStatusAsync(config, providerId, ct);
        if (!status.IsAuthenticated)
        {
            AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthStatusSignedOut(providerId).EscapeMarkup()}[/]");
            if (status.FailureMessage is { } failure)
                AnsiConsole.MarkupLine($"[grey]{failure.EscapeMarkup()}[/]");
            return 0;
        }
        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthStatusSignedIn(Account(status), Plan(status)).EscapeMarkup()}[/]");
        if (await auth.ReadUsageAsync(config, providerId, ct) is { } usage)
            PrintUsage(usage);
        return 0;
    }

    private static async Task<int> LogoutAsync(ProviderAuthenticationCoordinator auth, AppConfig config,
        string providerId, CancellationToken ct)
    {
        await auth.LogoutAsync(config, providerId, ct);
        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthLogoutSuccess(providerId).EscapeMarkup()}[/]");
        return 0;
    }

    private static async Task<int> VerifyAsync(ProviderAuthenticationCoordinator auth, IModelRuntimeResolver resolver,
        AppConfig config, string? providerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new InvalidOperationException("Select a provider to verify.");
        await auth.GetAccessTokenAsync(config, providerId, forceRefresh: true, ct);
        if (!(await auth.GetStatusAsync(config, providerId, ct)).IsAuthenticated)
            throw new InvalidOperationException($"Provider '{providerId}' is not signed in.");
        var model = ModelPreferenceRules.Find(config.ProviderPreferences, providerId)?.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"Select a model for provider '{providerId}'.");
        var catalog = await ModelProviderCatalog.FetchAsync(config, resolver, providerId, ModelCatalogRefreshStrategy.Online, ct);
        if (!catalog.Success)
            throw new InvalidOperationException(catalog.ErrorMessage ?? $"Could not load the model catalog of provider '{providerId}'.");
        if (!catalog.Models.Any(entry => string.Equals(entry.Id, model, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Model '{model}' is not listed in the catalog of provider '{providerId}'.");
        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthVerifySuccess(model).EscapeMarkup()}[/]");
        return 0;
    }

    private static void ShowEvent(ProviderLoginEvent notification, bool noBrowser)
    {
        if (notification.Kind == "device_code" && notification.UserCode is { } code && notification.Url is { } verificationUrl)
            AnsiConsole.MarkupLine(FallbackText.AuthLoginDeviceCode(code, verificationUrl).EscapeMarkup());
        else if (notification.Kind == "auth_url" && notification.Url is { } authorizationUrl)
        {
            AnsiConsole.MarkupLine(FallbackText.AuthLoginUrl(authorizationUrl).EscapeMarkup());
            AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthLoginWaiting.EscapeMarkup()}[/]");
        }
        else if (notification.Message is { } message)
            AnsiConsole.MarkupLine(message.EscapeMarkup());
        if (!noBrowser && notification.Kind is "auth_url" or "device_code" && notification.Url is { } url)
            TryOpenBrowser(url);
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

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private static void PrintUsage(ProviderUsageSnapshot usage)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[white]{FallbackText.AuthUsageHeader.EscapeMarkup()}[/]");
        foreach (var displayWindow in ProviderUsageWindowPresentation.Shape(usage))
            PrintWindow(WindowLabel(displayWindow.Kind), displayWindow.Window);

        if (usage.Credits is { HasCredits: true } credits)
        {
            var balance = credits.Unlimited
                ? FallbackText.AuthUsageCreditsUnlimited
                : FallbackText.AuthUsageCreditsBalance(credits.Balance ?? "—");
            AnsiConsole.MarkupLine($"  [white]Credits[/]    {balance.EscapeMarkup()}");
        }

        if (!string.IsNullOrEmpty(usage.LimitReachedKind))
            AnsiConsole.MarkupLine($"[yellow]{FallbackText.AuthUsageLimitReached(usage.LimitReachedKind).EscapeMarkup()}[/]");
    }

    private static string WindowLabel(ProviderUsageWindowKind kind) => kind switch
    {
        ProviderUsageWindowKind.FiveHour => FallbackText.AuthUsageWindowFiveHour,
        ProviderUsageWindowKind.Weekly => FallbackText.AuthUsageWindowWeekly,
        ProviderUsageWindowKind.Primary => FallbackText.AuthUsageWindowPrimary,
        ProviderUsageWindowKind.Secondary => FallbackText.AuthUsageWindowSecondary,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static void PrintWindow(string label, ProviderRateLimitWindow window)
    {
        var bar = BuildBar(window.UsedPercent);
        var color = ColorForUsage(window.UsedPercent);
        var resetIn = FormatTimespan(window.ResetAt - DateTimeOffset.UtcNow);
        AnsiConsole.MarkupLine(
            $"  [white]{label.EscapeMarkup()}[/]  [{color}]{bar}[/] [{color}]{window.UsedPercent,3}%[/]  [grey]resets in {resetIn}[/]");
    }

    private static string BuildBar(int usedPercent)
    {
        const int width = 10;
        var filled = Math.Clamp((int)Math.Round(usedPercent / 10.0), 0, width);
        return new string('▰', filled) + new string('▱', width - filled);
    }

    private static string ColorForUsage(int usedPercent)
    {
        var remaining = 100 - usedPercent;
        if (remaining < 20) return "red";
        if (remaining < 40) return "yellow";
        return "green";
    }

    private static string FormatTimespan(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "now";
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalMinutes}m";
    }

    private static string Account(ProviderAuthenticationStatus status)
    {
        if (!string.IsNullOrEmpty(status.Email))
            return status.Email;
        if (!string.IsNullOrEmpty(status.DisplayName))
            return status.DisplayName;
        var accountId = status.AccountId;
        if (string.IsNullOrEmpty(accountId))
            return "(no account id)";
        return accountId.Length <= 8 ? accountId : accountId[..4] + "…" + accountId[^4..];
    }

    private static string Plan(ProviderAuthenticationStatus status) =>
        string.IsNullOrEmpty(status.PlanType) ? "unknown" : status.PlanType;

    private sealed class NoContributions : IContributionView
    {
        public IReadOnlyList<TContract> Resolve<TContract>(string? threadId = null)
            where TContract : class, IContributionContract => [];
        public IReadOnlyList<ContributionEntry<TContract>> ResolveEntries<TContract>(string? threadId = null)
            where TContract : class, IContributionContract => [];
        public long GetRevision<TContract>() where TContract : class, IContributionContract => 0;
    }
}
