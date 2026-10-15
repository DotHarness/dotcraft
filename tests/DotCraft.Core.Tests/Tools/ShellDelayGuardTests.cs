using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ShellDelayGuardTests
{
    private readonly string _root = Path.GetTempPath();

    [Theory]
    [InlineData("sleep 2 && echo done")]
    [InlineData("Start-Sleep -Milliseconds 2500")]
    [InlineData("timeout /t 5 /nobreak")]
    public async Task Exec_RejectsALeadingDelay(string command)
    {
        var terminals = new CountingTerminalService();

        var result = await Tools(terminals).Exec(command);

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.Equal(0, terminals.Starts);
    }

    [Theory]
    [InlineData("sleep 1", false)]
    [InlineData("echo start; sleep 5", false)]
    [InlineData("sleep 30", true)]
    public async Task Exec_AllowsShortEmbeddedOrBackgroundDelays(string command, bool runInBackground)
    {
        var terminals = new CountingTerminalService();

        var result = await Tools(terminals).Exec(command, runInBackground: runInBackground);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, terminals.Starts);
    }

    private ShellTools Tools(IBackgroundTerminalService terminals) =>
        new(_root, terminals, requireApprovalOutsideWorkspace: false, approvalService: new AutoApproveApprovalService());

    private sealed class CountingTerminalService : StubBackgroundTerminalService
    {
        public CountingTerminalService()
        {
            StartHandler = (request, _) =>
            {
                Starts++;
                return Task.FromResult(new BackgroundTerminalSnapshot
                {
                    SessionId = "term_counted",
                    Command = request.Command,
                    Status = BackgroundTerminalStatus.Completed,
                    ExitCode = 0,
                    Output = "ok"
                });
            };
        }

        public int Starts { get; private set; }
    }
}
