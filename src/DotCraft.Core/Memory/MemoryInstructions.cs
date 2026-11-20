namespace DotCraft.Memory;

internal static class MemoryInstructions
{
    public static string FileFormat(IReadOnlyList<MemoryTypeDefinition> types, bool markPersonal = false) =>
$"""
Each memory is one Markdown file holding one fact, named `<name>.md`, with frontmatter:

---
name: <short-kebab-case-slug, same as the file name>
description: <one-line summary, used to decide relevance later>
metadata:
  type: {string.Join(" | ", types.Select(static type => type.Name))}
---

<the fact>

{string.Join("\n", types.Select(type => $"- {type.Name}{(markPersonal && type.Personal ? " (personal)" : string.Empty)}: {type.Description}"))}
""";
}
