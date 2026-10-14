namespace DotCraft.Memory;

internal static class MemoryInstructions
{
    public const string FileFormat =
"""
Each memory is one Markdown file holding one fact, named `<name>.md`, with frontmatter:

---
name: <short-kebab-case-slug, same as the file name>
description: <one-line summary, used to decide relevance later>
metadata:
  type: user | feedback | project | reference
---

<the fact. For feedback and project memories, follow with **Why:** and **How to apply:** lines. Link related memories with [[name]].>

- user: who the user is (role, expertise, preferences).
- feedback: how the user wants you to work, both corrections and approaches they confirmed; include the why.
- project: ongoing goals, decisions, or constraints the repository does not record; convert relative dates to absolute dates.
- reference: where to find information outside the workspace (URLs, dashboards, trackers).
""";
}
