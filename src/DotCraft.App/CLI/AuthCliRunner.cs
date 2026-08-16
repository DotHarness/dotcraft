using DotCraft.Auth.OpenAI;
using DotCraft.Hub;
using DotCraft.Text;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// Implements <c>dotcraft auth openai &lt;login|logout|status&gt;</c>.
/// Operates against the global <c>~/.craft/config.json</c> + <c>~/.craft/auth.json</c>; no workspace required.
/// </summary>
public static class AuthCliRunner
{
    public static async Task<int> RunAsync(CommandLineArgs cliArgs, CancellationToken cancellationToken)
    {
        var provider = cliArgs.AuthProvider?.Trim().ToLowerInvariant();
        if (provider is null or "")
        {
            AnsiConsole.MarkupLine($"[red]{FallbackText.AuthUsage}[/]");
            return 64;
        }

        if (!string.Equals(provider, "openai", StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[red]{FallbackText.AuthOpenAiUnsupported}[/]");
            return 64;
        }

        var action = cliArgs.AuthAction?.Trim().ToLowerInvariant() ?? "login";
        var providerId = string.IsNullOrWhiteSpace(cliArgs.AuthProviderId)
            ? "openai"
            : cliArgs.AuthProviderId.Trim();

        var globalConfigPath = InitHelper.GetGlobalConfigPath();
        var authService = new OpenAIAuthManager(
            new OpenAITokenStore(HubPaths.ForCurrentUser().CraftHomePath));

        return action switch
        {
            "login" => await HandleLoginAsync(authService, providerId, globalConfigPath, cliArgs.AuthNoBrowser, cliArgs.AuthNoBind, cancellationToken),
            "logout" => await HandleLogoutAsync(authService, providerId, globalConfigPath, cancellationToken),
            "status" => await HandleStatusAsync(authService, cliArgs.AuthNoUsage, cancellationToken),
            _ => UsageError()
        };
    }

    private static int UsageError()
    {
        AnsiConsole.MarkupLine($"[red]{FallbackText.AuthUsage}[/]");
        return 64;
    }

    private static async Task<int> HandleLoginAsync(
        OpenAIAuthManager authService,
        string providerId,
        string globalConfigPath,
        bool noBrowser,
        bool noBind,
        CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthOpenAiLoginStarting}[/]");

        OpenAIAuthStatus status;
        try
        {
            status = await authService.LoginAsync(
                openBrowser: !noBrowser,
                onAuthorizationUrl: url =>
                {
                    AnsiConsole.MarkupLine(FallbackText.AuthOpenAiLoginUrl(url).EscapeMarkup());
                    AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthOpenAiLoginWaiting(OpenAIAuthConstants.RedirectPortPrimary).EscapeMarkup()}[/]");
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[yellow]{FallbackText.AuthOpenAiLoginCancelled}[/]");
            return 130;
        }
        catch (OpenAIAuthException ex)
        {
            AnsiConsole.MarkupLine($"[red]{FallbackText.AuthOpenAiLoginFailed(ex.Message).EscapeMarkup()}[/]");
            return 1;
        }

        var accountDisplay = MaskAccount(status.AccountId);
        var planDisplay = string.IsNullOrEmpty(status.PlanType) ? "unknown" : status.PlanType!;

        AnsiConsole.MarkupLine($"[green]✓ {FallbackText.AuthOpenAiLoginSuccess(accountDisplay, planDisplay).EscapeMarkup()}[/]");

        if (!noBind)
        {
            OpenAIAuthBindingPersistence.BindProviderToOAuth(
                providerId,
                OpenAIProviderProjection.ToProviderStatus(status),
                globalConfigPath);
            AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthOpenAiLoginBound(providerId).EscapeMarkup()}[/]");
        }
        return 0;
    }

    private static async Task<int> HandleLogoutAsync(
        OpenAIAuthManager authService,
        string providerId,
        string globalConfigPath,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(cancellationToken);
        OpenAIAuthBindingPersistence.UnbindProvider(providerId, globalConfigPath);
        AnsiConsole.MarkupLine($"[green]✓ {FallbackText.AuthOpenAiLogoutSuccess}[/]");
        AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthOpenAiLogoutUnbound(providerId).EscapeMarkup()}[/]");
        return 0;
    }

    private static async Task<int> HandleStatusAsync(OpenAIAuthManager authService, bool skipUsage, CancellationToken cancellationToken)
    {
        var status = authService.GetStatus();
        if (!status.LoggedIn)
        {
            AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthOpenAiStatusSignedOut}[/]");
            return 0;
        }

        var account = MaskAccount(status.AccountId);
        var plan = string.IsNullOrEmpty(status.PlanType) ? "unknown" : status.PlanType!;
        var lastRefresh = status.LastRefresh?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
        AnsiConsole.MarkupLine($"[green]{FallbackText.AuthOpenAiStatusSignedIn(account, plan, lastRefresh).EscapeMarkup()}[/]");

        if (status.AccessTokenExpiresAt is { } expires)
            AnsiConsole.MarkupLine($"[grey]Access token expires {expires.ToLocalTime():yyyy-MM-dd HH:mm}.[/]");

        if (skipUsage)
            return 0;

        await PrintUsageAsync(authService, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task PrintUsageAsync(OpenAIAuthManager authService, CancellationToken cancellationToken)
    {
        var usageClient = new OpenAIUsageClient(authService);
        OpenAIUsageSnapshot snapshot;
        try
        {
            snapshot = await usageClient.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OpenAIAuthException ex)
        {
            AnsiConsole.MarkupLine($"[grey]{FallbackText.AuthOpenAiUsageUnavailable(ex.Message).EscapeMarkup()}[/]");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[white]{FallbackText.AuthOpenAiUsageHeader}[/]");
        foreach (var displayWindow in OpenAIUsageWindowPresentation.Shape(snapshot))
            PrintWindow(WindowLabel(displayWindow.Kind), displayWindow.Window);

        if (snapshot.Credits is { } credits && credits.HasCredits)
        {
            var balance = credits.Unlimited
                ? FallbackText.AuthOpenAiUsageCreditsUnlimited
                : FallbackText.AuthOpenAiUsageCreditsBalance(credits.Balance ?? "—");
            AnsiConsole.MarkupLine($"  [white]Credits[/]    {balance.EscapeMarkup()}");
        }

        if (!string.IsNullOrEmpty(snapshot.LimitReachedKind))
        {
            AnsiConsole.MarkupLine($"[yellow]{FallbackText.AuthOpenAiUsageLimitReached(snapshot.LimitReachedKind).EscapeMarkup()}[/]");
        }
    }

    private static string WindowLabel(OpenAIUsageWindowKind kind) => kind switch
    {
        OpenAIUsageWindowKind.FiveHour => FallbackText.AuthOpenAiUsageWindowFiveHour,
        OpenAIUsageWindowKind.Weekly => FallbackText.AuthOpenAiUsageWindowWeekly,
        OpenAIUsageWindowKind.Primary => FallbackText.AuthOpenAiUsageWindowPrimary,
        OpenAIUsageWindowKind.Secondary => FallbackText.AuthOpenAiUsageWindowSecondary,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static void PrintWindow(string label, RateLimitWindow window)
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

    private static string MaskAccount(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
            return "(no account id)";
        if (accountId.Length <= 8)
            return accountId;
        return accountId[..4] + "…" + accountId[^4..];
    }

}
