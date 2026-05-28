using DotCraft.Auth.OpenAI;
using DotCraft.Localization;
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
            AnsiConsole.MarkupLine($"[red]{Strings.AuthUsage}[/]");
            return 64;
        }

        if (!string.Equals(provider, "openai", StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[red]{Strings.AuthOpenAiUnsupported}[/]");
            return 64;
        }

        var action = cliArgs.AuthAction?.Trim().ToLowerInvariant() ?? "login";
        var providerId = string.IsNullOrWhiteSpace(cliArgs.AuthProviderId)
            ? "openai"
            : cliArgs.AuthProviderId.Trim();

        var globalConfigPath = InitHelper.GetGlobalConfigPath();
        var authService = new OpenAIAuthManager();

        return action switch
        {
            "login" => await HandleLoginAsync(authService, providerId, globalConfigPath, cliArgs.AuthNoBrowser, cancellationToken),
            "logout" => await HandleLogoutAsync(authService, providerId, globalConfigPath, cancellationToken),
            "status" => await HandleStatusAsync(authService, cliArgs.AuthNoUsage, cancellationToken),
            _ => UsageError()
        };
    }

    private static int UsageError()
    {
        AnsiConsole.MarkupLine($"[red]{Strings.AuthUsage}[/]");
        return 64;
    }

    private static async Task<int> HandleLoginAsync(
        OpenAIAuthManager authService,
        string providerId,
        string globalConfigPath,
        bool noBrowser,
        CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[green]{Strings.AuthOpenAiLoginStarting}[/]");

        OpenAIAuthStatus status;
        try
        {
            status = await authService.LoginAsync(
                openBrowser: !noBrowser,
                onAuthorizationUrl: url =>
                {
                    AnsiConsole.MarkupLine(Strings.AuthOpenAiLoginUrl(url).EscapeMarkup());
                    AnsiConsole.MarkupLine($"[grey]{Strings.AuthOpenAiLoginWaiting(OpenAIAuthConstants.RedirectPortPrimary).EscapeMarkup()}[/]");
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.AuthOpenAiLoginCancelled}[/]");
            return 130;
        }
        catch (OpenAIAuthException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Strings.AuthOpenAiLoginFailed(ex.Message).EscapeMarkup()}[/]");
            return 1;
        }

        var accountDisplay = MaskAccount(status.AccountId);
        var planDisplay = string.IsNullOrEmpty(status.PlanType) ? "unknown" : status.PlanType!;

        AnsiConsole.MarkupLine($"[green]✓ {Strings.AuthOpenAiLoginSuccess(accountDisplay, planDisplay).EscapeMarkup()}[/]");

        OpenAIAuthBindingPersistence.BindProviderToOAuth(providerId, status, globalConfigPath);
        AnsiConsole.MarkupLine($"[grey]{Strings.AuthOpenAiLoginBound(providerId).EscapeMarkup()}[/]");
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
        AnsiConsole.MarkupLine($"[green]✓ {Strings.AuthOpenAiLogoutSuccess}[/]");
        AnsiConsole.MarkupLine($"[grey]{Strings.AuthOpenAiLogoutUnbound(providerId).EscapeMarkup()}[/]");
        return 0;
    }

    private static async Task<int> HandleStatusAsync(OpenAIAuthManager authService, bool skipUsage, CancellationToken cancellationToken)
    {
        var status = authService.GetStatus();
        if (!status.LoggedIn)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.AuthOpenAiStatusSignedOut}[/]");
            return 0;
        }

        var account = MaskAccount(status.AccountId);
        var plan = string.IsNullOrEmpty(status.PlanType) ? "unknown" : status.PlanType!;
        var lastRefresh = status.LastRefresh?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
        AnsiConsole.MarkupLine($"[green]{Strings.AuthOpenAiStatusSignedIn(account, plan, lastRefresh).EscapeMarkup()}[/]");

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
            AnsiConsole.MarkupLine($"[grey]{Strings.AuthOpenAiUsageUnavailable(ex.Message).EscapeMarkup()}[/]");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[white]{Strings.AuthOpenAiUsageHeader}[/]");
        PrintWindow(Strings.AuthOpenAiUsageWindowFiveHour, snapshot.Primary);
        PrintWindow(Strings.AuthOpenAiUsageWindowWeekly, snapshot.Secondary);

        if (snapshot.Credits is { } credits && credits.HasCredits)
        {
            var balance = credits.Unlimited
                ? Strings.AuthOpenAiUsageCreditsUnlimited
                : Strings.AuthOpenAiUsageCreditsBalance(credits.Balance ?? "—");
            AnsiConsole.MarkupLine($"  [white]Credits[/]    {balance.EscapeMarkup()}");
        }

        if (!string.IsNullOrEmpty(snapshot.LimitReachedKind))
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.AuthOpenAiUsageLimitReached(snapshot.LimitReachedKind).EscapeMarkup()}[/]");
        }
    }

    private static void PrintWindow(string label, RateLimitWindow? window)
    {
        if (window is null)
        {
            AnsiConsole.MarkupLine($"  [white]{label.EscapeMarkup()}[/]  [grey](no data)[/]");
            return;
        }

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
