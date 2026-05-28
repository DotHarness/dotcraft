using DotCraft.Commands.Custom;
using DotCraft.Commands.Handlers;
using DotCraft.Localization;

namespace DotCraft.Commands.Core;

/// <summary>
/// Centralized command registry for built-in and custom commands.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _commandToCanonical = new(StringComparer.OrdinalIgnoreCase);
    private CustomCommandLoader? _customCommandLoader;

    /// <summary>
    /// Registers a command handler with optional metadata.
    /// </summary>
    public void RegisterHandler(ICommandHandler handler, CommandRegistration? metadata = null)
    {
        var effectiveMetadata = metadata ?? handler.Metadata ?? BuildFallbackMetadata(handler);
        var canonical = NormalizeCommandName(effectiveMetadata.Name);
        var aliases = BuildAliases(handler, effectiveMetadata, canonical);

        _registrations[canonical] = effectiveMetadata with { Name = canonical, Aliases = aliases };
        _handlers[canonical] = handler;
        _commandToCanonical[canonical] = canonical;

        foreach (var alias in aliases)
        {
            _handlers[alias] = handler;
            _commandToCanonical[alias] = canonical;
        }
    }

    /// <summary>
    /// Returns all known command names, including aliases and custom commands.
    /// </summary>
    public IReadOnlyList<string> GetKnownCommands()
    {
        var known = new HashSet<string>(_handlers.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in EnumerateCustomCommands())
            known.Add($"/{custom.Name}");
        return known.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Lists command metadata for help rendering and SDK discovery.
    /// </summary>
    /// <param name="context">When set, filters commands whose required services are unavailable.</param>
    /// <param name="language">When set, resolves descriptions for this language without mutating global <see cref="LanguageService.Current"/>.</param>
    public IReadOnlyList<CommandInfo> ListCommands(CommandContext? context = null, Language? language = null)
    {
        var result = new List<CommandInfo>();
        foreach (var registration in _registrations.Values.OrderBy(registration => registration.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsServiceAvailable(registration, context))
                continue;

            result.Add(new CommandInfo
            {
                Name = registration.Name,
                Aliases = registration.Aliases,
                Description = ResolveDescription(registration, language),
                Category = registration.Category,
                RequiresAdmin = registration.RequiresAdmin
            });
        }

        foreach (var custom in EnumerateCustomCommands())
        {
            result.Add(new CommandInfo
            {
                Name = $"/{custom.Name}",
                Aliases = [],
                Description = string.IsNullOrWhiteSpace(custom.Description)
                    ? "(no description)"
                    : custom.Description,
                Category = "custom",
                RequiresAdmin = false
            });
        }

        return result
            .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Tries to execute a slash command.
    /// </summary>
    public async Task<CommandResult> TryExecuteAsync(string rawText, CommandContext context, ICommandResponder responder)
    {
        var trimmedText = rawText.Trim();
        if (!trimmedText.StartsWith('/'))
            return CommandResult.NotHandled();

        var parts = trimmedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];

        context = context with
        {
            RawText = rawText,
            Command = cmd,
            Arguments = args,
            CommandRegistry = this
        };

        if (_handlers.TryGetValue(cmd, out var handler))
        {
            var canonical = _commandToCanonical.GetValueOrDefault(cmd, cmd);
            if (_registrations.TryGetValue(canonical, out var registration))
            {
                if (registration.RequiresAdmin && !context.IsAdmin)
                {
                    await responder.SendTextAsync(Strings.CommandPermissionDenied);
                    return CommandResult.HandledResult();
                }

                if (!IsServiceAvailable(registration, context))
                {
                    await responder.SendTextAsync(Strings.CommandServiceUnavailable);
                    return CommandResult.HandledResult();
                }
            }

            return await handler.HandleAsync(context, responder);
        }

        var resolved = _customCommandLoader?.TryResolve(trimmedText);
        if (resolved != null)
            return CommandResult.PromptExpansion(resolved.ExpandedPrompt);

        var msg = CommandHelper.FormatUnknownCommandMessage(rawText, [.. GetKnownCommands()]);
        await responder.SendTextAsync(msg);
        return CommandResult.HandledResult();
    }

    /// <summary>
    /// Tries to resolve a slash-command invocation into a prompt expansion using
    /// custom command definitions only.
    /// </summary>
    public string? TryResolvePromptExpansion(string rawText) =>
        _customCommandLoader?.TryResolve(rawText)?.ExpandedPrompt;

    /// <summary>
    /// Resolves a command registration by name or alias.
    /// </summary>
    public CommandRegistration? GetRegistration(string command)
    {
        var normalized = NormalizeCommandName(command);
        var canonical = _commandToCanonical.GetValueOrDefault(normalized, normalized);
        return _registrations.GetValueOrDefault(canonical);
    }

    /// <summary>
    /// Creates the default registry with all built-in handlers registered.
    /// </summary>
    public static CommandRegistry CreateDefault(CustomCommandLoader? customCommandLoader = null)
    {
        var registry = new CommandRegistry { _customCommandLoader = customCommandLoader };

        registry.RegisterHandler(new NewCommandHandler(), new CommandRegistration
        {
            Name = "/new",
            DescriptionKey = "cmd.new"
        });
        registry.RegisterHandler(new DebugCommandHandler(), new CommandRegistration
        {
            Name = "/debug",
            RequiresAdmin = true,
            DescriptionKey = "cmd.debug"
        });
        registry.RegisterHandler(new StopCommandHandler(), new CommandRegistration
        {
            Name = "/stop",
            RequiresAdmin = true,
            DescriptionKey = "command.stop.description"
        });
        registry.RegisterHandler(new HelpCommandHandler(), new CommandRegistration
        {
            Name = "/help",
            DescriptionKey = "cmd.help"
        });
        registry.RegisterHandler(new HeartbeatCommandHandler(), new CommandRegistration
        {
            Name = "/heartbeat",
            DescriptionKey = "cmd.heartbeat",
            RequiredService = "heartbeat"
        });
        registry.RegisterHandler(new CronCommandHandler(), new CommandRegistration
        {
            Name = "/cron",
            DescriptionKey = "cmd.cron_list",
            RequiredService = "cron"
        });

        return registry;
    }

    private List<CustomCommandInfo> EnumerateCustomCommands() =>
        _customCommandLoader?.ListCommands() ?? [];

    private static string NormalizeCommandName(string command) =>
        command.StartsWith('/') ? command.ToLowerInvariant() : $"/{command.ToLowerInvariant()}";

    private static string[] BuildAliases(ICommandHandler handler, CommandRegistration metadata, string canonical)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in metadata.Aliases)
        {
            var normalized = NormalizeCommandName(alias);
            if (!string.Equals(normalized, canonical, StringComparison.OrdinalIgnoreCase))
                aliases.Add(normalized);
        }

        foreach (var cmd in handler.Commands)
        {
            var normalized = NormalizeCommandName(cmd);
            if (!string.Equals(normalized, canonical, StringComparison.OrdinalIgnoreCase))
                aliases.Add(normalized);
        }

        return [.. aliases];
    }

    private static bool IsServiceAvailable(CommandRegistration registration, CommandContext? context)
    {
        if (context == null || string.IsNullOrWhiteSpace(registration.RequiredService))
            return true;

        return registration.RequiredService.ToLowerInvariant() switch
        {
            "heartbeat" => context.HeartbeatService != null,
            "cron" => context.CronService != null,
            _ => true
        };
    }

    private static string ResolveDescription(CommandRegistration registration, Language? language = null)
    {
        if (string.IsNullOrWhiteSpace(registration.DescriptionKey))
            return registration.Name;

        var localized = language.HasValue
            ? LanguageService.Translate(registration.DescriptionKey, language.Value)
            : LanguageService.Current.T(registration.DescriptionKey);
        if (!string.Equals(localized, registration.DescriptionKey, StringComparison.Ordinal))
            return localized;

        return registration.DescriptionKey switch
        {
            "command.stop.description" => Strings.CommandStopDescription,
            _ => registration.Name
        };
    }

    private static CommandRegistration BuildFallbackMetadata(ICommandHandler handler)
    {
        var commands = handler.Commands;
        var name = commands.Length > 0 ? NormalizeCommandName(commands[0]) : "/unknown";
        var aliases = commands.Skip(1).Select(NormalizeCommandName).ToArray();
        return new CommandRegistration
        {
            Name = name,
            Aliases = aliases,
            DescriptionKey = name
        };
    }
}

/// <summary>
/// Registration metadata for a command.
/// </summary>
public sealed record CommandRegistration
{
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public string DescriptionKey { get; init; } = string.Empty;
    public string Category { get; init; } = "builtin";
    public bool RequiresAdmin { get; init; }
    public string? RequiredService { get; init; }
}

/// <summary>
/// Wire-friendly command metadata for listing APIs and dynamic help.
/// </summary>
public sealed class CommandInfo
{
    public string Name { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "builtin";
    public bool RequiresAdmin { get; set; }
}
