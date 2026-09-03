using DotCraft.Commands.Core;
using DotCraft.Cron;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

internal sealed class CommandRequestHandler(
    CommandRegistry commandRegistry,
    ISessionService sessionService,
    AppServerConnection connection,
    CronService? cronService,
    string? workspaceCraftPath,
    Func<SessionThread, CancellationToken, Task<SessionWireThread>> enrichThreadAsync) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.CommandList, HandleCommandListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.CommandExecute, HandleCommandExecuteAsync);
    }

    private Task<object?> HandleCommandListAsync(AppServerTypedRequest<Contract.CommandListParams> request, CancellationToken ct)
    {
        _ = ct;
        var p = request.Params;
        var includeBuiltins = ValueOrDefault(p.IncludeBuiltins);

        var commands = commandRegistry.ListCommands()
            .Where(c => !IsUnavailableWorkspaceCommand(c.Name))
            .Where(c => includeBuiltins != false ||
                !string.Equals(c.Category, "builtin", StringComparison.OrdinalIgnoreCase))
            .Where(c =>
            {
                var reg = commandRegistry.GetRegistration(c.Name);
                return reg == null || IsServiceAvailableForRegistration(reg);
            })
            .Select(c => new Contract.CommandInfo
            {
                Name = c.Name,
                Aliases = new Protocol.Optional<IReadOnlyList<string>>(c.Aliases),
                DescriptionKey = c.DescriptionKey,
                FallbackDescription = c.FallbackDescription,
                Description = c.Description,
                Category = c.Category,
                RequiresAdmin = c.RequiresAdmin
            })
            .ToList();

        return Task.FromResult<object?>(new Contract.CommandListResult
        {
            Commands = new Protocol.Optional<IReadOnlyList<Contract.CommandInfo>>(commands)
        });
    }

    private bool IsUnavailableWorkspaceCommand(string commandName)
    {
        if (!string.Equals(commandName, "/init", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            return false;
        }

        var workspacePath = Directory.GetParent(workspaceCraftPath)?.FullName;
        return !string.IsNullOrWhiteSpace(workspacePath)
            && (File.Exists(Path.Combine(workspacePath, "AGENTS.override.md"))
                || File.Exists(Path.Combine(workspacePath, "AGENTS.md")));
    }

    private async Task<object?> HandleCommandExecuteAsync(AppServerTypedRequest<Contract.CommandExecuteParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId) ?? string.Empty;
        var command = ValueOrDefault(p.Command) ?? string.Empty;
        var arguments = ValueOrDefault(p.Arguments)?.ToList();
        var sender = ValueOrDefault(p.Sender);
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(command))
            throw AppServerErrors.InvalidParams("'command' is required.");

        var commandName = ExtractCommandName(command);
        var registration = commandRegistry.GetRegistration(commandName);

        if (registration != null && !IsSenderAllowed(registration, sender))
            throw AppServerErrors.CommandPermissionDenied(commandName);

        if (registration != null && !IsServiceAvailableForRegistration(registration))
            throw AppServerErrors.CommandServiceUnavailable(commandName);

        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var rawText = BuildRawText(command, arguments);
        var senderId = Read(sender?.SenderId) ?? thread.UserId ?? connection.ClientInfo?.Name ?? "anonymous";
        var senderName = Read(sender?.SenderName) ?? thread.UserId ?? connection.ClientInfo?.Name ?? "anonymous";
        var source = string.IsNullOrWhiteSpace(thread.OriginChannel)
            ? (connection.IsChannelAdapter ? connection.ChannelAdapterName ?? "external" : "appserver")
            : thread.OriginChannel;

        var context = new CommandContext
        {
            SessionId = threadId,
            RawText = rawText,
            UserId = senderId,
            UserName = senderName,
            IsAdmin = string.Equals(Read(sender?.SenderRole), "admin", StringComparison.OrdinalIgnoreCase),
            Source = source,
            GroupId = Read(sender?.GroupId),
            ChannelContext = thread.ChannelContext,
            WorkspacePath = thread.WorkspacePath,
            SessionService = sessionService,
            CronService = cronService,
            CommandRegistry = commandRegistry
        };

        var responder = new BufferedCommandResponder();
        var result = await commandRegistry.TryExecuteAsync(rawText, context, responder);
        SessionWireThread? resetThread = null;
        if (!string.IsNullOrWhiteSpace(result.NewThreadId))
        {
            try
            {
                var freshThread = await sessionService.GetThreadAsync(result.NewThreadId, ct);
                resetThread = await enrichThreadAsync(freshThread, ct);
            }
            catch
            {
                // Best-effort enrichment for command/execute response.
            }
        }

        return new Contract.CommandExecuteResult
        {
            Handled = result.Handled,
            Message = responder.Message ?? result.Message,
            IsMarkdown = responder.IsMarkdown || result.IsMarkdown,
            ExpandedPrompt = result.ExpandedPrompt,
            SessionReset = result.SessionReset,
            Thread = resetThread is null ? null : AppServerContractMapper.ToContract(resetThread),
            ArchivedThreadIds = new Protocol.Optional<IReadOnlyList<string>?>(result.ArchivedThreadIds?.ToArray()),
            CreatedLazily = result.CreatedLazily
        };
    }

    private bool IsServiceAvailableForRegistration(CommandRegistration registration)
    {
        return registration.RequiredService?.ToLowerInvariant() switch
        {
            "cron" => cronService != null,
            _ => true
        };
    }

    private static bool IsSenderAllowed(CommandRegistration registration, Contract.SenderContext? sender)
    {
        if (!registration.RequiresAdmin)
            return true;
        return string.Equals(Read(sender?.SenderRole), "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCommandName(string rawCommand)
    {
        var trimmed = rawCommand.Trim();
        if (trimmed.Length == 0)
            return rawCommand;

        var whitespaceIndex = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespaceIndex >= 0 ? trimmed[..whitespaceIndex] : trimmed;
    }

    private static string BuildRawText(string command, List<string>? arguments)
    {
        var normalized = command.StartsWith('/') ? command : $"/{command}";
        if (arguments == null || arguments.Count == 0)
            return normalized;
        return $"{normalized} {string.Join(" ", arguments)}";
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static string? Read(Protocol.Optional<string?>? value) =>
        value.HasValue && value.Value.IsSet ? value.Value.Value : null;

    private sealed class BufferedCommandResponder : ICommandResponder
    {
        private readonly List<(string Text, bool IsMarkdown)> _segments = [];

        /// <summary>
        /// All non-empty segments joined with newlines, or null if nothing was sent.
        /// </summary>
        public string? Message
        {
            get
            {
                var parts = _segments
                    .Where(s => !string.IsNullOrWhiteSpace(s.Text))
                    .Select(s => s.Text)
                    .ToList();
                return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
            }
        }

        /// <summary>
        /// True if any segment was sent as markdown.
        /// </summary>
        public bool IsMarkdown => _segments.Any(s => s.IsMarkdown);

        public Task SendTextAsync(string message)
        {
            _segments.Add((message, false));
            return Task.CompletedTask;
        }

        public Task SendMarkdownAsync(string markdown)
        {
            _segments.Add((markdown, true));
            return Task.CompletedTask;
        }
    }
}
