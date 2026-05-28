---
description: "Two-layer memory system with grep-based recall."
always: true
---

# Memory

## Structure

- `memory/MEMORY.md` — Long-term facts (preferences, project context, relationships). Always loaded into your context.
- `memory/HISTORY.md` — Append-only event log. **NOT** loaded into context; search it with grep when you need to recall past events.

## Search Past Events

Use `GrepFiles` to search HISTORY.md (cross-platform, no shell dependency):

```
GrepFiles(pattern="keyword", path="memory", include="HISTORY.md")
```

Regex patterns work too — e.g. `pattern="meeting|deadline|project"` matches any of those words.

For advanced search (e.g. context lines), use `Exec` with shell grep:

```bash
grep -i -C 3 "keyword" "memory/HISTORY.md"
```

Always search HISTORY.md when the user asks about past events, decisions, or conversations that aren't covered by MEMORY.md.

## When to Update MEMORY.md

Write important facts to MEMORY.md immediately when you learn them:
- User preferences ("I prefer dark mode", "call me Alex")
- Project context ("The API uses OAuth2", "deploy target is AWS")
- Key relationships ("Alice is the project lead")
- Recurring instructions ("Always write tests before merging")

Use `EditFile` or `WriteFile` to update MEMORY.md. Keep it concise and well-organized.

## Auto-consolidation

Old conversations are automatically summarized: long-term facts are extracted to MEMORY.md, and event entries are appended to HISTORY.md. You don't need to manage this process — just focus on writing important facts immediately when they come up.
