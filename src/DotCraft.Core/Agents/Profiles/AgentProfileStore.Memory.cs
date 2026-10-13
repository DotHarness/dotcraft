using System.Text.Json.Nodes;

namespace DotCraft.Agents;

public sealed partial class AgentProfileStore
{
    private static string? CompileMemoryScope(
        JsonObject frontmatter,
        string id,
        List<AgentProfileDiagnostic> diagnostics)
    {
        var raw = ReadOptionalString(frontmatter, "memory", diagnostics);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        switch (raw.Trim())
        {
            case "shared":
                return null;
            case "own":
                return AgentProfileName.IsValid(id) ? AgentProfileName.MemoryScope(id) : null;
            default:
                diagnostics.Add(Error("InvalidPolicyValue", "memory must be 'shared' or 'own'."));
                return null;
        }
    }
}
