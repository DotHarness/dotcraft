using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateExecCommand()
    {
        var options = new ExecOptions();
        var prompt = ExecPromptArgument();
        var ephemeral = Flag("--ephemeral", "Create a thread that is not persisted.");
        var command = new Command("exec", "Run one agent task non-interactively.")
        {
            prompt,
            ephemeral
        };
        options.AddTo(command);
        command.Subcommands.Add(CreateExecResumeCommand());
        command.Subcommands.Add(CreateExecInterruptCommand());
        command.SetAction((parseResult, cancellationToken) => RunExecAsync(
            parseResult,
            options,
            [prompt],
            parseResult.GetValue(prompt) ?? [],
            cancellationToken,
            ephemeral: parseResult.GetValue(ephemeral)));
        return command;
    }

    private static Command CreateExecResumeCommand()
    {
        var options = new ExecOptions();
        var threadId = new Argument<string?>("thread-id")
        {
            Description = "Thread to continue. Omit it when using --last.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var prompt = ExecPromptArgument();
        var last = Flag("--last", "Continue the most recently active thread in this workspace.");
        var command = new Command("resume", "Continue an existing thread with a new task.")
        {
            threadId,
            prompt,
            last
        };
        options.AddTo(command);
        command.Validators.Add(result =>
        {
            if (!result.GetValue(last) && string.IsNullOrWhiteSpace(result.GetValue(threadId)))
                result.AddError("Provide a thread id or use --last.");
        });
        command.SetAction((parseResult, cancellationToken) =>
        {
            var useLast = parseResult.GetValue(last);
            var first = parseResult.GetValue(threadId);
            var promptTokens = parseResult.GetValue(prompt) ?? [];
            return RunExecAsync(
                parseResult,
                options,
                [threadId, prompt],
                useLast && first != null ? [first, .. promptTokens] : promptTokens,
                cancellationToken,
                resumeThreadId: useLast ? null : first,
                resumeLast: useLast);
        });
        return command;
    }

    private static Argument<string[]> ExecPromptArgument() => new("prompt")
    {
        Description = "Prompt to send to the agent. Omit it or pass - to read the prompt from standard input.",
        Arity = ArgumentArity.ZeroOrMore
    };

    private static async Task<int> RunExecAsync(
        ParseResult parseResult,
        ExecOptions options,
        IReadOnlyList<Argument> positional,
        IReadOnlyList<string> promptTokens,
        CancellationToken cancellationToken,
        bool ephemeral = false,
        string? resumeThreadId = null,
        bool resumeLast = false)
    {
        ExecRequest request;
        try
        {
            if (FindUnrecognizedOption(parseResult, positional) is { } unrecognized)
                throw new ExecUsageException($"Unrecognized option '{unrecognized}'.");
            var images = ExecInput.ResolveImages(parseResult.GetValue(options.Images) ?? []);
            var prompt = await ExecInput.ResolvePromptAsync(promptTokens, cancellationToken);
            var workspace = Path.GetFullPath(parseResult.GetValue(options.WorkspaceDirectory) ?? Directory.GetCurrentDirectory());
            request = new ExecRequest
            {
                WorkspacePath = Path.TrimEndingDirectorySeparator(workspace),
                Prompt = prompt,
                Model = parseResult.GetValue(options.Model)?.Trim(),
                Images = images,
                AutoApprove = parseResult.GetValue(options.AutoApprove),
                Ephemeral = ephemeral,
                Json = parseResult.GetValue(options.Json),
                OutputLastMessagePath = parseResult.GetValue(options.OutputLastMessage) is { } output
                    ? Path.GetFullPath(output)
                    : null,
                ResumeThreadId = resumeThreadId,
                ResumeLast = resumeLast
            };
        }
        catch (ExecUsageException ex)
        {
            await parseResult.InvocationConfiguration.Error.WriteLineAsync(ex.Message);
            return UsageErrorExitCode;
        }

        return await RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Exec,
            Exec = request,
            RemoteUrl = parseResult.GetValue(options.Remote),
            Token = parseResult.GetValue(options.Token),
            ReservesStdout = true
        }, cancellationToken);
    }

    private static Command CreateExecInterruptCommand()
    {
        var workspaceDirectory = WorkspaceDirectoryOption();
        var remote = RemoteOption();
        var token = TokenOption();
        var threadId = new Argument<string?>("thread-id")
        {
            Description = "Thread whose running turn to stop. Omit it to stop the only running exec task in this workspace.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var command = new Command("interrupt", "Stop the running turn of a thread.")
        {
            threadId,
            workspaceDirectory,
            remote,
            token
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (FindUnrecognizedOption(parseResult, [threadId]) is { } unrecognized)
            {
                await parseResult.InvocationConfiguration.Error.WriteLineAsync($"Unrecognized option '{unrecognized}'.");
                return UsageErrorExitCode;
            }

            var workspace = Path.GetFullPath(parseResult.GetValue(workspaceDirectory) ?? Directory.GetCurrentDirectory());
            return await ExecInterrupt.RunAsync(
                Path.TrimEndingDirectorySeparator(workspace),
                parseResult.GetValue(threadId),
                new CommandLineArgs
                {
                    Mode = CommandLineArgs.RunMode.Exec,
                    RemoteUrl = parseResult.GetValue(remote),
                    Token = parseResult.GetValue(token)
                },
                cancellationToken);
        });
        return command;
    }

    private static Option<string?> WorkspaceDirectoryOption() => new("--cd", "-C")
    {
        Description = "Workspace directory to run in. Defaults to the current directory.",
        HelpName = "dir"
    };

    private static Option<string?> RemoteOption() =>
        StringOption("--remote", "Connect to an existing AppServer WebSocket endpoint.");

    private static Option<string?> TokenOption() =>
        StringOption("--token", "Authenticate to the remote AppServer with this token.");

    private static string? FindUnrecognizedOption(ParseResult parseResult, IReadOnlyList<Argument> positional)
    {
        var bound = new HashSet<object>(
            positional.SelectMany(argument => parseResult.GetResult(argument)?.Tokens ?? []),
            ReferenceEqualityComparer.Instance);
        foreach (var token in parseResult.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
                return null;
            if (token.Value.Length > 1 && token.Value[0] == '-' && bound.Contains(token))
                return token.Value;
        }

        return null;
    }

    private sealed class ExecOptions
    {
        public Option<string?> WorkspaceDirectory { get; } = WorkspaceDirectoryOption();

        public Option<string?> Model { get; } = CreateModelOption();

        public Option<string[]> Images { get; } = new("--image", "-i")
        {
            Description = "Attach local images to the prompt. Repeatable, and accepts a comma-separated list.",
            HelpName = "file"
        };

        public Option<bool> AutoApprove { get; } =
            Flag("--auto-approve", "Approve every operation that needs approval on this thread.");

        public Option<bool> Json { get; } =
            Flag("--json", "Write the event stream to standard output as JSON Lines.");

        public Option<string?> OutputLastMessage { get; } = new("--output-last-message", "-o")
        {
            Description = "Also write the final message to this file.",
            HelpName = "file"
        };

        public Option<string?> Remote { get; } = RemoteOption();

        public Option<string?> Token { get; } = TokenOption();

        public void AddTo(Command command)
        {
            command.Options.Add(WorkspaceDirectory);
            command.Options.Add(Model);
            command.Options.Add(Images);
            command.Options.Add(AutoApprove);
            command.Options.Add(Json);
            command.Options.Add(OutputLastMessage);
            command.Options.Add(Remote);
            command.Options.Add(Token);
        }

        private static Option<string?> CreateModelOption()
        {
            var model = new Option<string?>("--model", "-m")
            {
                Description = "Model to use on the workspace's default provider.",
                HelpName = "id"
            };
            model.Validators.Add(result =>
            {
                if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                    result.AddError("--model cannot be empty.");
            });
            return model;
        }
    }
}
