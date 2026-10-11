using System.Diagnostics;
using System.Text.Json;
using DotCraft.AppServer;
using Xunit;
using Rpc = DotCraft.Protocol.AppServer.AppServerRpc;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerCommandExecTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appserver_exec_{Guid.NewGuid():N}");
    private readonly AppServerTestHarness _harness = new();

    public AppServerCommandExecTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _harness.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Exec_ReturnsOutputAndExitCodeFromTheWorkspaceRoot()
    {
        File.WriteAllText(Path.Combine(_harness.Identity.WorkspacePath, "marker.txt"), "in-workspace");
        using var init = await _harness.InitializeAsync();
        Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("commandExec").GetBoolean());

        var result = await SendAsync(new
        {
            command = Shell(
                "echo %DOTCRAFT_EXEC_VALUE%& type marker.txt& echo problem 1>&2& exit /b 3",
                "echo $DOTCRAFT_EXEC_VALUE; cat marker.txt; echo problem >&2; exit 3"),
            env = new Dictionary<string, string?> { ["DOTCRAFT_EXEC_VALUE"] = "from-env" }
        });

        var stdout = result.GetProperty("stdout").GetString()!;
        Assert.Equal(3, result.GetProperty("exitCode").GetInt32());
        Assert.Equal(["from-env", "in-workspace"], stdout.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("problem", result.GetProperty("stderr").GetString()!.Trim());
    }

    [Fact]
    public async Task Exec_RejectsBadParamsAndReportsStartFailures()
    {
        var blocked = Path.Combine(_root, "secrets");
        Directory.CreateDirectory(blocked);
        _harness.Monitor.Current.Security.BlacklistedPaths.Add(blocked);
        await _harness.InitializeAsync();
        var command = Shell("echo hi", "echo hi");

        var empty = await SendForErrorAsync(new { command = Array.Empty<string>() });
        var relative = await SendForErrorAsync(new { command, cwd = "relative" });
        var notFound = await SendForErrorAsync(new { command = new[] { $"dotcraft-missing-{Guid.NewGuid():N}" } });
        var missingDirectory = await SendForErrorAsync(new { command, cwd = Path.Combine(_root, "missing") });
        var pathBlocked = await SendForErrorAsync(new { command, cwd = blocked });
        var notExecutable = await SendForErrorAsync(new { command = new[] { WriteInvalidProgram() } });

        Assert.Equal(AppServerErrors.InvalidParamsCode, empty.GetProperty("code").GetInt32());
        Assert.Equal(AppServerErrors.InvalidParamsCode, relative.GetProperty("code").GetInt32());
        Assert.All([notFound, missingDirectory, pathBlocked, notExecutable], error => Assert.Equal(AppServerErrors.CommandExecErrorCode, error.GetProperty("code").GetInt32()));
        Assert.Equal("CommandNotFound", DataCode(notFound));
        Assert.Equal("CommandNotExecutable", DataCode(notExecutable));
        Assert.Equal("DirectoryNotFound", DataCode(missingDirectory));
        Assert.Equal("PathBlocked", DataCode(pathBlocked));
    }

    [Fact]
    public async Task Exec_TerminatesTheProcessWhenTheTimeoutElapses()
    {
        await _harness.InitializeAsync();
        var stopwatch = Stopwatch.StartNew();

        var result = await SendAsync(new
        {
            command = Shell("echo started& ping -n 30 127.0.0.1 >nul", "echo started; sleep 30"),
            timeoutMs = 1000
        });

        Assert.Equal(124, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("started", result.GetProperty("stdout").GetString()!.Trim());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public async Task Exec_KeepsAtMostTheOutputCap()
    {
        await _harness.InitializeAsync();

        var result = await SendAsync(new { command = Shell("echo 0123456789ABCDEF", "echo 0123456789ABCDEF"), outputBytesCap = 10 });

        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("0123456789", result.GetProperty("stdout").GetString());
    }

    [Fact]
    public async Task Exec_DoesNotWaitForAProgramThatKeepsTheOutputOpen()
    {
        await _harness.InitializeAsync();
        var stopwatch = Stopwatch.StartNew();

        var result = await SendAsync(new
        {
            command = Shell("start /b ping -n 9 127.0.0.1 >nul & echo done", "sleep 8 & echo done")
        });

        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("done", result.GetProperty("stdout").GetString()!.Trim());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public async Task Exec_PassesArgumentsToABatchFileLiterally()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var script = WriteEchoScript();
        await _harness.InitializeAsync();
        string[] arguments = ["a b", "x & echo injected", "%PATH%", "say \"hi\"", "caret^", "plain", ""];
        const string expected = "\"a b\" \"x & echo injected\" \"%PATH%\" \"say \"\"hi\"\"\" \"caret^\" plain \"\"";

        var byPath = await SendAsync(new { command = new[] { script }.Concat(arguments) });
        var byName = await SendAsync(new
        {
            command = new[] { "echo-args" }.Concat(arguments),
            env = new Dictionary<string, string?> { ["PATH"] = Path.GetDirectoryName(script) }
        });

        Assert.All([byPath, byName], result =>
        {
            Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
            Assert.Equal([expected], result.GetProperty("stdout").GetString()!.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
        });
    }

    [Fact]
    public async Task Exec_RejectsALineBreakInABatchFileArgument()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var script = WriteEchoScript();
        await _harness.InitializeAsync();

        var error = await SendForErrorAsync(new { command = new[] { script, "one\r\necho injected" } });

        Assert.Equal(AppServerErrors.InvalidParamsCode, error.GetProperty("code").GetInt32());
    }

    private string WriteEchoScript()
    {
        var directory = Path.Combine(_root, "batch dir");
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "echo-args.cmd");
        File.WriteAllText(script, "@echo(%*\r\n");
        return script;
    }

    private string WriteInvalidProgram()
    {
        var program = Path.Combine(_root, "not-a-program.exe");
        File.WriteAllText(program, "not a program");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return program;
    }

    private static string[] Shell(string windows, string unix) =>
        OperatingSystem.IsWindows() ? ["cmd", "/d", "/c", windows] : ["/bin/sh", "-c", unix];

    private async Task<JsonElement> SendAsync(object parameters)
    {
        using var response = await ExecuteAsync(parameters);
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }

    private async Task<JsonElement> SendForErrorAsync(object parameters)
    {
        using var response = await ExecuteAsync(parameters);
        Assert.True(response.RootElement.TryGetProperty("error", out var error), response.RootElement.GetRawText());
        return error.Clone();
    }

    private async Task<JsonDocument> ExecuteAsync(object parameters)
    {
        await _harness.ExecuteRequestAsync(_harness.BuildRequest(Rpc.CommandExec.Name, parameters));
        return _harness.Transport.TryReadSent()!;
    }

    private static string? DataCode(JsonElement error) =>
        error.GetProperty("data").GetProperty("code").GetString();
}
