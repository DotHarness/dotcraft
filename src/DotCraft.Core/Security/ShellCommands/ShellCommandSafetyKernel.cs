namespace DotCraft.Security.ShellCommands;

public sealed class ShellCommandSafetyKernel
{
    private readonly ShellIdentityResolver _resolver;

    private readonly CommandPlatform _platform;

    private readonly PosixScriptLowerer _posix = new();

    private readonly PowerShellScriptLowerer _powerShell = new();

    private readonly CmdScriptSplitter _cmd = new();

    private readonly DangerousCommandDetector _danger;

    public ShellCommandSafetyKernel()
        : this(ShellIdentityResolver.Host, ShellKindExtensions.HostPlatform())
    {
    }

    public ShellCommandSafetyKernel(ShellIdentityResolver resolver, CommandPlatform platform)
    {
        _resolver = resolver;
        _platform = platform;
        _danger = new DangerousCommandDetector(_posix);
    }

    private IShellScriptLowerer LowererFor(ShellFamily family) => family switch
    {
        ShellFamily.PowerShell => _powerShell,
        ShellFamily.Cmd => _cmd,
        _ => _posix
    };

    public ShellAssessment Evaluate(ShellSafetyRequest request)
    {
        var shell = request.ResolvedShell;
        if (shell is null && !_resolver.TryResolve(request.ShellSelector, out shell, out var shellReason))
        {
            return new ShellAssessment
            {
                Decision = ShellDecision.Forbidden,
                Reasons = [shellReason]
            };
        }

        var lowering = LowererFor(shell.Family).Lower(request.Command);
        var commands = lowering.PlainCommands
            ?? [new[] { ShellScriptSentinels.For(shell.Family), request.Command }];
        var matches = new List<ShellRuleMatch>();
        var reasons = new List<string>();
        var risk = ShellRiskLevel.None;
        foreach (var command in commands)
        {
            AssessCommand(command, lowering, shell, request, matches, reasons, ref risk);
        }

        var overall = matches.Count == 0 ? ShellDecision.Allow : matches.Max(match => match.Decision);
        var approvalKey = ShellApprovalKey.Create(
            shell,
            request.WorkingDirectory,
            lowering,
            request.Command,
            request.Policy.Fingerprint);

        return new ShellAssessment
        {
            Decision = overall,
            Reasons = reasons.Distinct(StringComparer.Ordinal).ToArray(),
            Shell = shell,
            Lowering = lowering,
            Matches = matches,
            ApprovalKey = approvalKey,
            Risk = overall == ShellDecision.Allow ? ShellRiskLevel.None : risk,
            Remember = BuildRememberProposal(matches, lowering)
        };
    }

    private void AssessCommand(
        IReadOnlyList<string> command,
        LoweredScript lowering,
        ShellIdentity shell,
        ShellSafetyRequest request,
        List<ShellRuleMatch> matches,
        List<string> reasons,
        ref ShellRiskLevel risk)
    {
        var ruleMatches = request.Policy.Match(command, _platform);
        if (ruleMatches.Count > 0)
        {
            matches.AddRange(ruleMatches);
            foreach (var match in ruleMatches)
            {
                if (match.Decision == ShellDecision.Allow)
                    continue;
                risk = Max(risk, ShellRiskLevel.Rule);
                reasons.Add(RuleReason(match));
            }

            return;
        }

        var dangerMatch = lowering.IsPlain
            ? _danger.Match(command, shell.Family, _platform)
            : _danger.MatchAny(lowering, _platform);
        if (dangerMatch is not null)
        {
            risk = ShellRiskLevel.Dangerous;
            var decision = request.AutoApprovesPrompts ? ShellDecision.Forbidden : ShellDecision.Prompt;
            var reason = request.AutoApprovesPrompts
                ? $"{dangerMatch.Reason} without an interactive approval; add an allow rule to run it unattended."
                : $"{dangerMatch.Reason} without approval.";
            matches.Add(new ShellFallbackMatch(command, decision, reason, Dangerous: true));
            reasons.Add(reason);
            return;
        }

        if (request.Workspace.Contains(request.WorkingDirectory))
        {
            matches.Add(new ShellFallbackMatch(command, ShellDecision.Allow, "workspace"));
            return;
        }

        risk = Max(risk, ShellRiskLevel.OutsideWorkspace);
        var decisionOutside = request.RequireApprovalOutsideWorkspace ? ShellDecision.Prompt : ShellDecision.Forbidden;
        const string reasonOutside = "Working directory is outside the workspace boundary.";
        matches.Add(new ShellFallbackMatch(command, decisionOutside, reasonOutside));
        reasons.Add(reasonOutside);
    }

    private ShellRememberProposal BuildRememberProposal(IReadOnlyList<ShellRuleMatch> matches, LoweredScript lowering)
    {
        var rules = new List<ShellPrefixRule>();
        var exactFallback = false;
        foreach (var match in matches.Where(match => match.Decision == ShellDecision.Prompt))
        {
            if (match is ShellFallbackMatch { Dangerous: true })
            {
                exactFallback = true;
                continue;
            }

            var prefix = match is ShellPrefixRuleMatch ruleMatch
                ? ruleMatch.Rule.Prefix
                : lowering.IsPlain ? match.Command : null;
            if (prefix is null || ShellPolicy.IsBannedPrefix(prefix, _platform))
            {
                exactFallback = true;
                continue;
            }

            var rule = new ShellPrefixRule(prefix, ShellDecision.Allow);
            if (!rules.Contains(rule))
                rules.Add(rule);
        }

        return new ShellRememberProposal(rules, exactFallback);
    }

    private static string RuleReason(ShellPrefixRuleMatch match)
    {
        var prefix = string.Join(' ', match.Rule.Prefix);
        var verb = match.Decision == ShellDecision.Forbidden ? "forbids" : "requires approval for";
        return match.Rule.Justification is { } justification
            ? $"Policy {verb} commands starting with '{prefix}': {justification}."
            : $"Policy {verb} commands starting with '{prefix}'.";
    }

    private static ShellRiskLevel Max(ShellRiskLevel left, ShellRiskLevel right) =>
        left >= right ? left : right;
}
