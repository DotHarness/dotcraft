namespace DotCraft.Agents;

/// <summary>
/// The conversational builder's working-draft shape. Mirrors the Agent Profile frontmatter
/// (see specs/agents/agent-profiles.md) and the renderer's <c>ProfileDraft</c>
/// (desktop/src/renderer/components/agents/agentProfileDraft.ts) field-for-field, so a draft edited
/// by the builder tools round-trips through both the renderer's parser and the real
/// <see cref="AgentProfileStore"/> YAML parser. Operational <c>mode</c> is intentionally absent —
/// a profile expresses its posture through tools/mcp/skills scope and approval policy, not Agent/Plan.
/// </summary>
public sealed class AgentProfileDraft
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Avatar { get; set; }
    public string Model { get; set; } = "inherit";
    public string ReasoningEffort { get; set; } = "medium";

    public List<string> ToolsAllow { get; set; } = [];
    public List<string> ToolsDeny { get; set; } = [];
    public string AgentControl { get; set; } = "full";

    public List<string> McpServers { get; set; } = [];
    public List<string> McpToolsAllow { get; set; } = [];
    public List<string> McpToolsDeny { get; set; } = [];

    public List<string> SkillsPreload { get; set; } = [];
    public List<string> SkillsAllow { get; set; } = [];
    public List<string> SkillsDeny { get; set; } = [];

    public string ApprovalPolicy { get; set; } = "default";
    public bool RequireApprovalOutsideWorkspace { get; set; }

    public string RoleInstructions { get; set; } = string.Empty;
}

/// <summary>
/// Parses and re-serializes an Agent Profile Markdown document (YAML frontmatter + Markdown body)
/// for the conversational builder. The serializer emits the same flat shape the renderer emits —
/// scalar <c>key: value</c> lines and inline <c>[a, b]</c> flow sequences — which is valid YAML for
/// <see cref="AgentProfileStore"/> and also readable by the renderer's indent-based parser.
/// </summary>
public static class AgentProfileDraftEditor
{
    private static readonly string[] ApprovalPolicies = ["default", "autoApprove", "interrupt"];
    private static readonly string[] AgentControls = ["full", "disabled", "allowList"];
    private static readonly string[] ReasoningEfforts = ["minimal", "low", "medium", "high"];

    public static IReadOnlyList<string> ApprovalPolicyValues => ApprovalPolicies;
    public static IReadOnlyList<string> AgentControlValues => AgentControls;
    public static IReadOnlyList<string> ReasoningEffortValues => ReasoningEfforts;

    public static bool IsApprovalPolicy(string value) => ApprovalPolicies.Contains(value, StringComparer.Ordinal);
    public static bool IsAgentControl(string value) => AgentControls.Contains(value, StringComparer.Ordinal);
    public static bool IsReasoningEffort(string value) => ReasoningEfforts.Contains(value, StringComparer.Ordinal);

    /// <summary>Reads a raw profile Markdown document into an editable draft. Missing frontmatter yields an empty draft whose body is the whole text.</summary>
    public static AgentProfileDraft Parse(string? rawContent)
    {
        var draft = new AgentProfileDraft();
        var text = rawContent ?? string.Empty;

        var (frontmatter, body, hasFrontmatter) = SplitFrontmatter(text);
        if (!hasFrontmatter)
        {
            draft.RoleInstructions = text.Trim();
            return draft;
        }

        draft.RoleInstructions = body.Trim();

        string? section = null;
        string? sub = null;
        foreach (var rawLine in frontmatter.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var line = rawLine.Trim();
            var ci = line.IndexOf(':');
            if (ci < 0)
                continue;

            var key = line[..ci].Trim();
            var val = line[(ci + 1)..].Trim();

            if (indent == 0)
            {
                section = null;
                sub = null;
                switch (key)
                {
                    case "name": draft.Name = val; break;
                    case "description": draft.Description = val; break;
                    case "model": draft.Model = string.IsNullOrEmpty(val) ? "inherit" : val; break;
                    case "avatar": draft.Avatar = ParsePackedAvatar(val); break;
                    case "reasoning" or "tools" or "mcp" or "skills" or "permissions": section = key; break;
                }
            }
            else if (indent == 2)
            {
                sub = null;
                switch (section)
                {
                    case "reasoning" when key == "effort": draft.ReasoningEffort = string.IsNullOrEmpty(val) ? "medium" : val; break;
                    case "tools" when key == "allow": draft.ToolsAllow = ParseList(val); break;
                    case "tools" when key == "deny": draft.ToolsDeny = ParseList(val); break;
                    case "tools" when key == "agentControl": draft.AgentControl = string.IsNullOrEmpty(val) ? "full" : val; break;
                    case "mcp" when key == "servers": draft.McpServers = ParseList(val); break;
                    case "mcp" when key == "tools": sub = "mcpTools"; break;
                    case "skills" when key == "preload": draft.SkillsPreload = ParseList(val); break;
                    case "skills" when key == "allow": draft.SkillsAllow = ParseList(val); break;
                    case "skills" when key == "deny": draft.SkillsDeny = ParseList(val); break;
                    case "permissions" when key == "approvalPolicy": draft.ApprovalPolicy = string.IsNullOrEmpty(val) ? "default" : val; break;
                    case "permissions" when key == "requireApprovalOutsideWorkspace": draft.RequireApprovalOutsideWorkspace = val == "true"; break;
                }
            }
            else if (indent >= 4 && sub == "mcpTools")
            {
                if (key == "allow") draft.McpToolsAllow = ParseList(val);
                else if (key == "deny") draft.McpToolsDeny = ParseList(val);
            }
        }

        return draft;
    }

    /// <summary>Renders the draft as the raw Markdown an <c>agent/profiles/upsert</c> would persist.</summary>
    public static string ToMarkdown(AgentProfileDraft draft)
    {
        var fm = new List<string> { "---" };
        fm.Add($"name: {(string.IsNullOrEmpty(draft.Name) ? "untitled-agent" : draft.Name)}");
        fm.Add($"description: {draft.Description}");
        if (draft.Avatar.HasValue)
            fm.Add($"avatar: {draft.Avatar.Value}");
        fm.Add($"model: {(string.IsNullOrEmpty(draft.Model) ? "inherit" : draft.Model)}");

        if (!string.IsNullOrEmpty(draft.ReasoningEffort) && draft.ReasoningEffort != "medium")
        {
            fm.Add("reasoning:");
            fm.Add($"  effort: {draft.ReasoningEffort}");
        }

        if (draft.ToolsAllow.Count > 0 || draft.ToolsDeny.Count > 0 || draft.AgentControl != "full")
        {
            fm.Add("tools:");
            if (draft.ToolsAllow.Count > 0) fm.Add($"  allow: {YamlList(draft.ToolsAllow)}");
            if (draft.ToolsDeny.Count > 0) fm.Add($"  deny: {YamlList(draft.ToolsDeny)}");
            if (draft.AgentControl != "full") fm.Add($"  agentControl: {draft.AgentControl}");
        }

        if (draft.McpServers.Count > 0 || draft.McpToolsAllow.Count > 0 || draft.McpToolsDeny.Count > 0)
        {
            fm.Add("mcp:");
            if (draft.McpServers.Count > 0) fm.Add($"  servers: {YamlList(draft.McpServers)}");
            if (draft.McpToolsAllow.Count > 0 || draft.McpToolsDeny.Count > 0)
            {
                fm.Add("  tools:");
                if (draft.McpToolsAllow.Count > 0) fm.Add($"    allow: {YamlList(draft.McpToolsAllow)}");
                if (draft.McpToolsDeny.Count > 0) fm.Add($"    deny: {YamlList(draft.McpToolsDeny)}");
            }
        }

        if (draft.SkillsPreload.Count > 0 || draft.SkillsAllow.Count > 0 || draft.SkillsDeny.Count > 0)
        {
            fm.Add("skills:");
            if (draft.SkillsPreload.Count > 0) fm.Add($"  preload: {YamlList(draft.SkillsPreload)}");
            if (draft.SkillsAllow.Count > 0) fm.Add($"  allow: {YamlList(draft.SkillsAllow)}");
            if (draft.SkillsDeny.Count > 0) fm.Add($"  deny: {YamlList(draft.SkillsDeny)}");
        }

        fm.Add("permissions:");
        fm.Add($"  approvalPolicy: {draft.ApprovalPolicy}");
        fm.Add($"  requireApprovalOutsideWorkspace: {(draft.RequireApprovalOutsideWorkspace ? "true" : "false")}");
        fm.Add("---");

        return $"{string.Join('\n', fm)}\n\n{draft.RoleInstructions.Trim()}\n";
    }

    /// <summary>Adds items to a list if absent (ordinal, order-preserving). Returns the items that were newly added.</summary>
    public static List<string> AddTo(List<string> list, IEnumerable<string> items)
    {
        var added = new List<string>();
        foreach (var raw in items)
        {
            var item = raw?.Trim();
            if (string.IsNullOrEmpty(item))
                continue;
            if (list.Contains(item, StringComparer.Ordinal))
                continue;
            list.Add(item);
            added.Add(item);
        }
        return added;
    }

    /// <summary>Removes items from a list (ordinal). Returns the items that were actually removed.</summary>
    public static List<string> RemoveFrom(List<string> list, IEnumerable<string> items)
    {
        var removed = new List<string>();
        foreach (var raw in items)
        {
            var item = raw?.Trim();
            if (string.IsNullOrEmpty(item))
                continue;
            if (list.RemoveAll(x => string.Equals(x, item, StringComparison.Ordinal)) > 0)
                removed.Add(item);
        }
        return removed;
    }

    private static (string Frontmatter, string Body, bool HasFrontmatter) SplitFrontmatter(string text)
    {
        // Mirror the renderer regex: ^---\n(front)\n---\n?(body)$
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal)
            && !normalized.StartsWith("---\r", StringComparison.Ordinal)
            && normalized != "---")
        {
            // Accept a leading "---" line only.
            if (!normalized.StartsWith("---", StringComparison.Ordinal))
                return (string.Empty, text, false);
        }

        var firstNewline = normalized.IndexOf('\n');
        if (firstNewline < 0 || normalized[..firstNewline].Trim() != "---")
            return (string.Empty, text, false);

        var rest = normalized[(firstNewline + 1)..];
        var closeIndex = FindClosingFence(rest);
        if (closeIndex < 0)
            return (string.Empty, text, false);

        var frontmatter = rest[..closeIndex].TrimEnd('\n');
        var afterFence = rest[(closeIndex)..];
        // afterFence starts with the closing "---" line; drop it.
        var nl = afterFence.IndexOf('\n');
        var body = nl < 0 ? string.Empty : afterFence[(nl + 1)..];
        return (frontmatter, body, true);
    }

    private static int FindClosingFence(string rest)
    {
        var index = 0;
        foreach (var line in rest.Split('\n'))
        {
            if (line.Trim() == "---")
                return index;
            index += line.Length + 1;
        }
        return -1;
    }

    private static List<string> ParseList(string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(v) || v == "[]")
            return [];
        var inner = v.TrimStart('[').TrimEnd(']');
        return inner.Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }

    private static string YamlList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "[]" : $"[{string.Join(", ", values)}]";

    private static int? ParsePackedAvatar(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(ch => !char.IsDigit(ch))
            || !int.TryParse(value, out var parsed))
        {
            return null;
        }

        return AgentProfileAvatarCodec.TryDecode(parsed, out _, out _, out _) ? parsed : null;
    }
}
