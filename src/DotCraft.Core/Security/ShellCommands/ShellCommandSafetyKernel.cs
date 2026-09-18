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
        if (!_resolver.TryResolve(request.ShellSelector, out var shell, out var shellReason))
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
        var scanner = new PathEvidenceScanner(request.Workspace);
        var matches = new List<ShellRuleMatch>();
        var reasons = new List<string>();
        var risk = ShellRiskLevel.None;

        foreach (var command in commands)
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

                continue;
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
                continue;
            }

            var evidence = lowering.IsPlain
                ? scanner.ScanWords(command).Concat(TraversalEvidence(command, request.WorkingDirectory)).ToList()
                : scanner.ScanText(request.Command).Concat(TraversalEvidence([request.Command], request.WorkingDirectory)).ToList();
            var blacklisted = request.Blacklist is { } blacklist
                ? evidence.FirstOrDefault(item => blacklist.IsBlacklisted(item.ResolvedFullPath))
                : null;
            if (blacklisted is not null)
            {
                var reason = $"Command references the blacklisted path '{blacklisted.Original}'.";
                matches.Add(new ShellFallbackMatch(command, ShellDecision.Forbidden, reason));
                reasons.Add(reason);
                continue;
            }

            var outside = scanner.OutsideWorkspace(evidence);
            var cwdInside = request.Workspace.Contains(request.WorkingDirectory);
            if (outside.Count == 0 && cwdInside)
            {
                matches.Add(new ShellFallbackMatch(command, ShellDecision.Allow, "workspace"));
                continue;
            }

            risk = Max(risk, ShellRiskLevel.OutsideWorkspace);
            var outsideReason = cwdInside
                ? $"Command references paths outside the workspace: {string.Join(", ", outside.Select(item => item.Original))}."
                : "Working directory is outside the workspace boundary.";
            var outsideDecision = request.RequireApprovalOutsideWorkspace ? ShellDecision.Prompt : ShellDecision.Forbidden;
            matches.Add(new ShellFallbackMatch(command, outsideDecision, outsideReason));
            reasons.Add(outsideReason);
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

    private static IEnumerable<PathEvidence> TraversalEvidence(IReadOnlyList<string> words, string workingDirectory)
    {
        foreach (var word in words)
        {
            if (!word.Contains("../", StringComparison.Ordinal) && !word.Contains("..\\", StringComparison.Ordinal))
                continue;

            var candidate = words.Count == 1 && word.Contains(' ') ? ".." : word;
            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(workingDirectory, candidate));
            }
            catch
            {
                resolved = Path.GetFullPath(Path.Combine(workingDirectory, ".."));
            }

            yield return new PathEvidence(word, resolved, IsUnc: false);
        }
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
