using System.Text.RegularExpressions;

namespace DotCraft.Security.ShellCommands;

public sealed class ShellCommandSafetyKernel
{
    private const string UnknownDirectoryReason =
        "Command changes the working directory to a location that cannot be determined.";

    private const string UnknownDirectoryAfterChangeReason =
        "Command runs in a working directory that cannot be determined.";

    private static readonly Regex ClimbPattern =
        new(@"(?<![^\s""'/\\=:])\.\.(?![^\s""'/\\;&|)])", RegexOptions.Compiled);

    private static readonly HashSet<string> PosixDirectoryChangeCommands =
        new(StringComparer.Ordinal) { "cd", "pushd", "popd" };

    private static readonly HashSet<string> PowerShellDirectoryChangeCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "chdir", "sl", "pushd", "popd", "Set-Location", "Push-Location", "Pop-Location"
    };

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
        var scanner = new PathEvidenceScanner(request.Workspace);
        var matches = new List<ShellRuleMatch>();
        var reasons = new List<string>();
        var risk = ShellRiskLevel.None;
        var cwd = request.WorkingDirectoryIsKnown ? request.WorkingDirectory : null;
        var opaqueDirectoryChange = !lowering.IsPlain && ChangesDirectory(lowering.LiteralCommands, shell.Family);

        foreach (var command in commands)
        {
            var change = lowering.IsPlain ? DirectoryChangeOf(command, shell.Family, cwd) : null;
            AssessCommand(
                command,
                cwd,
                change,
                unknownTarget: change is { Destination: null } || opaqueDirectoryChange,
                lowering,
                shell,
                scanner,
                request,
                matches,
                reasons,
                ref risk);
            if (change is not null)
                cwd = change.Destination;
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
            WorkingDirectoryAfter = opaqueDirectoryChange ? null : cwd,
            Remember = BuildRememberProposal(matches, lowering)
        };
    }

    private sealed record DirectoryChange(string? Target, string? Destination);

    private static HashSet<string> DirectoryChangeNames(ShellFamily family) =>
        family == ShellFamily.Posix ? PosixDirectoryChangeCommands : PowerShellDirectoryChangeCommands;

    private static bool ChangesDirectory(IReadOnlyList<IReadOnlyList<string>> commands, ShellFamily family)
    {
        var names = DirectoryChangeNames(family);
        return commands.Any(command => command.Count > 0 && names.Contains(command[0]));
    }

    private static DirectoryChange? DirectoryChangeOf(IReadOnlyList<string> command, ShellFamily family, string? cwd)
    {
        if (command.Count == 0)
            return null;
        if (!DirectoryChangeNames(family).Contains(command[0]))
            return null;
        if (command[0].Equals("popd", StringComparison.OrdinalIgnoreCase)
            || command[0].Equals("Pop-Location", StringComparison.OrdinalIgnoreCase))
            return new DirectoryChange(null, null);

        var target = command.Skip(1).FirstOrDefault(word => !word.StartsWith('-'));
        if (target is null || cwd is null)
            return new DirectoryChange(target, null);
        try
        {
            return new DirectoryChange(target, Path.GetFullPath(Path.Combine(cwd, target)));
        }
        catch
        {
            return new DirectoryChange(target, null);
        }
    }

    private void AssessCommand(
        IReadOnlyList<string> command,
        string? cwd,
        DirectoryChange? change,
        bool unknownTarget,
        LoweredScript lowering,
        ShellIdentity shell,
        PathEvidenceScanner scanner,
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

        var baseDirectory = cwd ?? request.WorkingDirectory;
        var evidence = lowering.IsPlain
            ? scanner.ScanWords(command).Concat(TraversalEvidence(command, baseDirectory)).ToList()
            : scanner.ScanText(request.Command).Concat(TraversalEvidence([request.Command], baseDirectory)).ToList();
        if (change is { Target: { } target, Destination: { } destination })
            evidence.Add(new PathEvidence(target, destination, IsUnc: false));
        var blacklisted = request.Blacklist is { } blacklist
            ? evidence.FirstOrDefault(item => blacklist.IsBlacklisted(item.ResolvedFullPath))
            : null;
        if (blacklisted is not null)
        {
            var reason = $"Command references the blacklisted path '{blacklisted.Original}'.";
            matches.Add(new ShellFallbackMatch(command, ShellDecision.Forbidden, reason));
            reasons.Add(reason);
            return;
        }

        var outsideDecision = request.RequireApprovalOutsideWorkspace ? ShellDecision.Prompt : ShellDecision.Forbidden;
        if (cwd is null || unknownTarget)
        {
            var reason = cwd is null ? UnknownDirectoryAfterChangeReason : UnknownDirectoryReason;
            risk = Max(risk, ShellRiskLevel.OutsideWorkspace);
            matches.Add(new ShellFallbackMatch(command, outsideDecision, reason));
            reasons.Add(reason);
            return;
        }

        var outside = scanner.OutsideWorkspace(evidence);
        var cwdInside = request.Workspace.Contains(cwd);
        if (outside.Count == 0 && cwdInside)
        {
            matches.Add(new ShellFallbackMatch(command, ShellDecision.Allow, "workspace"));
            return;
        }

        risk = Max(risk, ShellRiskLevel.OutsideWorkspace);
        var outsideReason = cwdInside
            ? $"Command references paths outside the workspace: {string.Join(", ", outside.Select(item => item.Original))}."
            : "Working directory is outside the workspace boundary.";
        matches.Add(new ShellFallbackMatch(command, outsideDecision, outsideReason));
        reasons.Add(outsideReason);
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
            if (!ClimbPattern.IsMatch(word))
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
