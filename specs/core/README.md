# Core Specifications

Core specs define DotCraft's durable session model and workspace memory behavior.
They are the source of truth for lifecycle, persistence, and agent-facing state
that other protocols and clients project.

| File | Purpose |
|------|---------|
| [session-core.md](session-core.md) | Thread -> Turn -> Item domain model, fork/worktree ownership, lifecycle, events, persistence, approvals, and wire support boundaries. |
| [goal-design.md](goal-design.md) | Long-running thread goal model, state machine, tool surface, AppServer projection, and client UX contract. |
| [memory-consolidation.md](memory-consolidation.md) | Automatic and manual memory consolidation triggers, persistence, events, and UX surface. |
| [dreams.md](dreams.md) | Dream memory product model, runtime lifecycle, AppServer surface, and Desktop/dashboard UX contracts. |
| [context-export-cli.md](context-export-cli.md) | Local read-only CLI for exporting session/memory context and searching persisted session evidence. |

Related domains: [Protocols](../protocols/), [Runtime](../runtime/), [Agents](../agents/).

