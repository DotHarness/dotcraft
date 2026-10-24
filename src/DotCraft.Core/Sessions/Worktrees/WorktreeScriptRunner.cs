using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DotCraft.Processes;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Sessions;

internal sealed record WorktreeScriptResult(int? ExitCode, string OutputTail, bool Cancelled)
{
    public bool Succeeded => !Cancelled && ExitCode == 0;
}

internal static class WorktreeScriptRunner
{
    public const string SourceTreeVariable = "DOTCRAFT_SOURCE_TREE_PATH";
    public const string WorktreeVariable = "DOTCRAFT_WORKTREE_PATH";
    private const int TailCharacters = 64 * 1024;
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    public static async Task<WorktreeScriptResult> RunAsync(
        string script,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? variables,
        Action<string>? output,
        CancellationToken ct)
    {
        var tail = new OutputTail(output);
        if (!ShellIdentityResolver.Host.TryResolve(null, out var shell, out var reason))
            return tail.Fail(reason);
        if (!Directory.Exists(workingDirectory))
            return tail.Fail($"The folder '{workingDirectory}' does not exist.");
        if (ct.IsCancellationRequested)
            return new WorktreeScriptResult(null, string.Empty, Cancelled: true);

        var info = CreateStartInfo(shell, script, workingDirectory);
        info.Environment.Remove(SourceTreeVariable);
        info.Environment.Remove(WorktreeVariable);
        foreach (var (name, value) in variables ?? new Dictionary<string, string>())
            info.Environment[name] = value;

        ManagedChildProcess child;
        try
        {
            child = ManagedChildProcess.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return tail.Fail(ex.Message);
        }

        await using (child)
        {
            var process = child.Process;
            process.StandardInput.Close();
            var readers = Task.WhenAll(
                PumpAsync(process.StandardOutput, tail),
                PumpAsync(process.StandardError, tail));
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                }

                await Task.WhenAny(readers, Task.Delay(DrainGrace)).ConfigureAwait(false);
                return new WorktreeScriptResult(null, tail.ToString(), Cancelled: true);
            }

            await Task.WhenAny(readers, Task.Delay(DrainGrace)).ConfigureAwait(false);
            return new WorktreeScriptResult(process.ExitCode, tail.ToString(), Cancelled: false);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ShellIdentity shell, string script, string workingDirectory)
    {
        var info = new ProcessStartInfo(shell.ExecutablePath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        switch (shell.Family)
        {
            case ShellFamily.PowerShell:
                var wrapped = string.Join(
                    '\n',
                    "if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }",
                    "$ErrorActionPreference = 'Stop'",
                    "$ProgressPreference = 'SilentlyContinue'",
                    "$PSNativeCommandUseErrorActionPreference = $true",
                    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
                    script,
                    "if ($LASTEXITCODE) { exit $LASTEXITCODE }");
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-ExecutionPolicy", "Bypass", "-EncodedCommand" })
                    info.ArgumentList.Add(argument);
                info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)));
                break;
            case ShellFamily.Cmd:
                var commands = script
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static line => line.Length > 0);
                info.Arguments = "/d /s /c \"" + string.Join(" && ", commands) + "\"";
                break;
            default:
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("set -eo pipefail\n" + script.ReplaceLineEndings("\n"));
                break;
        }

        return info;
    }

    private static async Task PumpAsync(StreamReader reader, OutputTail tail)
    {
        try
        {
            await foreach (var chunk in TerminalOutputBuffer.ReadChunksAsync(reader, CancellationToken.None).ConfigureAwait(false))
                tail.Append(chunk);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private sealed class OutputTail(Action<string>? output)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public void Append(string chunk)
        {
            lock (_gate)
            {
                _buffer.Append(chunk);
                if (_buffer.Length > TailCharacters * 2)
                    _buffer.Remove(0, _buffer.Length - TailCharacters);
                output?.Invoke(chunk);
            }
        }

        public WorktreeScriptResult Fail(string message)
        {
            Append(message + Environment.NewLine);
            return new WorktreeScriptResult(null, ToString(), Cancelled: false);
        }

        public override string ToString()
        {
            lock (_gate)
            {
                var start = Math.Max(0, _buffer.Length - TailCharacters);
                if (start > 0 && char.IsLowSurrogate(_buffer[start]))
                    start++;
                return _buffer.ToString(start, _buffer.Length - start);
            }
        }
    }
}
