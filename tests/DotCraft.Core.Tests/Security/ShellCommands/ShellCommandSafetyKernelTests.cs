using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellCommandSafetyKernelTests : IDisposable
{
    private static readonly string WindowsSystemRoot =
        Path.Combine(Path.GetTempPath(), "shell-kernel-system-root");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shell_kernel_{Guid.NewGuid():N}");

    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"shell_outside_{Guid.NewGuid():N}");

    private readonly WorkspaceBoundary _workspace;

    public ShellCommandSafetyKernelTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        _workspace = new WorkspaceBoundary([_root]);
    }

    [Fact]
    public void Evaluate_UnknownShellSelector_IsForbiddenWithoutAShell()
    {
        var assessment = Posix().Evaluate(Request("ls", shell: "fish"));

        Assert.Equal(ShellDecision.Forbidden, assessment.Decision);
        Assert.Null(assessment.Shell);
        Assert.Contains("fish", assessment.ReasonText);
    }

    [Fact]
    public void Evaluate_PlainCommandInsideTheWorkspace_IsAllowed()
    {
        var assessment = Posix().Evaluate(Request("git status"));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.Equal(ShellRiskLevel.None, assessment.Risk);
        Assert.Equal(new[] { "git", "status" }, assessment.ApprovalKey!.CanonicalCommand);
    }

    [Fact]
    public void Evaluate_ForcedRemove_PromptsAsDangerous()
    {
        var assessment = Posix().Evaluate(Request("rm -rf build"));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_ForcedRemove_RemembersOnlyTheExactKey()
    {
        var remember = Posix().Evaluate(Request("rm -rf build")).Remember;

        Assert.Empty(remember.Rules);
        Assert.True(remember.ExactKeyFallback);
    }

    [Fact]
    public void Evaluate_ForcedRemoveWhenThePromptWouldBeAutoApproved_IsForbidden()
    {
        var assessment = Posix().Evaluate(Request("rm -rf build", autoApprovesPrompts: true));

        Assert.Equal(ShellDecision.Forbidden, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_PromptRule_PromptsAndProposesTheRulePrefixAsAllowed()
    {
        var policy = new ShellPolicy([new ShellPrefixRule(["git", "push"], ShellDecision.Prompt)]);

        var assessment = Posix().Evaluate(Request("git push origin main", policy: policy));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Rule, assessment.Risk);
        Assert.IsType<ShellPrefixRuleMatch>(Assert.Single(assessment.Matches));
        Assert.Equal(
            new[] { new ShellPrefixRule(["git", "push"], ShellDecision.Allow) },
            assessment.Remember.Rules);
    }

    [Fact]
    public void Evaluate_ForbiddenRule_IsForbidden()
    {
        var policy = new ShellPolicy([new ShellPrefixRule(["rm"], ShellDecision.Forbidden)]);

        var assessment = Posix().Evaluate(Request("rm -rf x", policy: policy));

        Assert.Equal(ShellDecision.Forbidden, assessment.Decision);
        Assert.Contains("forbids", assessment.ReasonText);
    }

    [Fact]
    public void Evaluate_AllowRule_BypassesDangerDetection()
    {
        var policy = new ShellPolicy([new ShellPrefixRule(["rm"], ShellDecision.Allow)]);

        var assessment = Posix().Evaluate(Request("rm -rf x", policy: policy));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.NotEqual(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_MultipleCommands_KeepsOneMatchPerCommandAndTakesTheMostSevere()
    {
        var assessment = Posix().Evaluate(Request("git status && rm -rf build"));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.All(assessment.Matches, match => Assert.IsType<ShellFallbackMatch>(match));
        Assert.Equal(
            new[] { ShellDecision.Allow, ShellDecision.Prompt },
            assessment.Matches.Select(match => match.Decision));
    }

    [Fact]
    public void Evaluate_MultipleCommandsWhereOneIsForbidden_IsForbidden()
    {
        var policy = new ShellPolicy([new ShellPrefixRule(["./evil"], ShellDecision.Forbidden)]);

        var assessment = Posix().Evaluate(Request("git status && ./evil", policy: policy));

        Assert.Equal(ShellDecision.Forbidden, assessment.Decision);
    }

    [Fact]
    public void Evaluate_OpaqueScript_IsKeyedByTheSentinelAndTheWholeScript()
    {
        const string Script = "for f in *; do echo $f; done";

        var assessment = Posix().Evaluate(Request(Script));

        Assert.False(assessment.Lowering!.IsPlain);
        Assert.Equal(new[] { ShellScriptSentinels.Posix, Script }, assessment.ApprovalKey!.CanonicalCommand);
        Assert.Equal(ShellDecision.Allow, assessment.Decision);
    }

    [Fact]
    public void Evaluate_DangerousLiteralInsideAnOpaqueScript_PromptsAsDangerous()
    {
        var assessment = Posix().Evaluate(Request("if test -d build; then rm -rf build; fi"));

        Assert.False(assessment.Lowering!.IsPlain);
        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_ForcedRemoveAfterAppendAssignment_PromptsAsDangerous()
    {
        var assessment = Posix().Evaluate(Request("TARGET+=build rm -rf build"));

        Assert.False(assessment.Lowering!.IsPlain);
        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_PowerShellForcedDelete_PromptsAsDangerous()
    {
        var assessment = Windows().Evaluate(Request("Remove-Item build -Force"));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_PowerShellObservation_IsAllowed()
    {
        var assessment = Windows().Evaluate(Request("Get-ChildItem -Recurse"));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.Equal(ShellKind.Pwsh, assessment.Shell!.Kind);
    }

    [Fact]
    public void Evaluate_CmdForcedDelete_PromptsAsDangerous()
    {
        var assessment = Windows().Evaluate(Request("del /f x.txt", shell: "cmd"));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Dangerous, assessment.Risk);
    }

    [Fact]
    public void Evaluate_CmdScript_IsAlwaysKeyedAsOpaque()
    {
        var assessment = Windows().Evaluate(Request("dir", shell: "cmd"));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.Equal(
            new[] { ShellScriptSentinels.Cmd, "dir" },
            assessment.ApprovalKey!.CanonicalCommand);
    }

    [Fact]
    public void Evaluate_SameCommandUnderDifferentPolicies_ProducesDifferentApprovalKeys()
    {
        var first = Posix().Evaluate(Request(
            "git status",
            policy: new ShellPolicy([new ShellPrefixRule(["npm", "test"], ShellDecision.Allow)])));
        var second = Posix().Evaluate(Request(
            "git status",
            policy: new ShellPolicy([new ShellPrefixRule(["yarn", "test"], ShellDecision.Allow)])));

        Assert.NotEqual(first.ApprovalKey!.Hash, second.ApprovalKey!.Hash);
    }

    [Fact]
    public void Evaluate_PlainCommandStartedOutsideWorkspace_ProposesItsOwnWordsAsAnAllowRule()
    {
        var remember = Posix().Evaluate(Request("cat notes.txt", workingDirectory: _outside)).Remember;

        var rule = Assert.Single(remember.Rules);
        Assert.Equal(new[] { "cat", "notes.txt" }, rule.Prefix);
        Assert.Equal(ShellDecision.Allow, rule.Decision);
        Assert.False(remember.ExactKeyFallback);
    }

    [Fact]
    public void Evaluate_ResolvedShell_IsUsedInsteadOfResolvingTheSelector()
    {
        var shell = new ShellIdentity(ShellKind.Zsh, "/opt/zsh");

        var assessment = Posix().Evaluate(Request("git status", shell: "fish", resolvedShell: shell));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.Same(shell, assessment.Shell);
    }

    [Theory]
    [InlineData(true, ShellDecision.Prompt)]
    [InlineData(false, ShellDecision.Forbidden)]
    public void Evaluate_LaunchDirectoryOutsideWorkspace_UsesBoundaryPolicy(bool requireApproval, ShellDecision expected)
    {
        var assessment = Posix().Evaluate(Request("git status", workingDirectory: _outside,
            requireApprovalOutsideWorkspace: requireApproval));

        Assert.Equal(expected, assessment.Decision);
        Assert.Equal(ShellRiskLevel.OutsideWorkspace, assessment.Risk);
    }

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    [InlineData("cmd")]
    public void Evaluate_WindowsOptionsAndPaths_DoNotRequireApproval(string shell)
    {
        string[] commands =
        [
            "shutdown /s /t 60", "shutdown.exe /s /t 60", "ipconfig /all", "cmd /c echo probe",
            @"type Z:\example\input.txt", @"type \\server\share\file.txt",
            @"type %USERPROFILE%\file.txt", @"Get-Content $env:USERPROFILE\file.txt",
            "echo ../outside", "cd ..; Get-Content secret.txt", "popd", "cd",
            "if ($true) { Set-Location ..; Get-Content secret.txt }"
        ];
        foreach (var command in commands)
        {
            var assessment = Windows().Evaluate(Request(command, shell: shell,
                requireApprovalOutsideWorkspace: false));

            Assert.True(assessment.Decision == ShellDecision.Allow, $"{shell}: {command}: {assessment.ReasonText}");
            Assert.Equal(ShellRiskLevel.None, assessment.Risk);
            Assert.Empty(assessment.Reasons);
        }
    }

    [Theory]
    [InlineData("cat /example/input.txt")]
    [InlineData("echo ../outside")]
    [InlineData("cat ~/secret $HOME/secret")]
    [InlineData("cd .. && cat secret.txt")]
    [InlineData("cd && cat x")]
    [InlineData("for f in /example/*; do cat $f; done")]
    [InlineData("for d in a b; do cd $d; done")]
    public void Evaluate_PosixPathsAndDirectoryChanges_DoNotRequireApproval(string command)
    {
        var assessment = Posix().Evaluate(Request(command, requireApprovalOutsideWorkspace: false));

        Assert.Equal(ShellDecision.Allow, assessment.Decision);
        Assert.Empty(assessment.Reasons);
    }

    [Theory]
    [InlineData(ShellDecision.Prompt)]
    [InlineData(ShellDecision.Forbidden)]
    public void Evaluate_WindowsSlashOptions_StillHonorExplicitRules(ShellDecision decision)
    {
        var policy = new ShellPolicy([new ShellPrefixRule(["shutdown"], decision)]);

        var assessment = Windows().Evaluate(Request("shutdown /s /t 60", policy: policy));

        Assert.Equal(decision, assessment.Decision);
        Assert.Equal(ShellRiskLevel.Rule, assessment.Risk);
    }

    [Fact]
    public void Evaluate_OpaqueCommandStartedOutsideWorkspace_RemembersOnlyExactKey()
    {
        var assessment = Posix().Evaluate(Request("for f in /example/*; do cat $f; done", workingDirectory: _outside));

        Assert.Equal(ShellDecision.Prompt, assessment.Decision);
        Assert.Empty(assessment.Remember.Rules);
        Assert.True(assessment.Remember.ExactKeyFallback);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _root, _outside })
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private ShellSafetyRequest Request(
        string command,
        string? shell = null,
        string? workingDirectory = null,
        ShellPolicy? policy = null,
        bool requireApprovalOutsideWorkspace = true,
        bool autoApprovesPrompts = false,
        ShellIdentity? resolvedShell = null) => new()
        {
            Command = command,
            ShellSelector = shell,
            ResolvedShell = resolvedShell,
            WorkingDirectory = workingDirectory ?? _root,
            Workspace = _workspace,
            Policy = policy ?? ShellPolicy.Empty,
            RequireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace,
            AutoApprovesPrompts = autoApprovesPrompts
        };

    private static ShellCommandSafetyKernel Posix() =>
        new(
            new ShellIdentityResolver(new ShellExecutableProbe(
                IsWindows: false,
                SystemRoot: null,
                FileExists: _ => true,
                FindOnPath: name => "/usr/bin/" + name)),
            CommandPlatform.Posix);

    private static ShellCommandSafetyKernel Windows() =>
        new(
            new ShellIdentityResolver(new ShellExecutableProbe(
                IsWindows: true,
                SystemRoot: WindowsSystemRoot,
                FileExists: _ => true,
                FindOnPath: name => Path.Combine(WindowsSystemRoot, name))),
            CommandPlatform.Windows);
}
