# Extension Specifications

Extension specs define how DotCraft discovers, loads, secures, and presents
plugin-like contributions. They own local extension packaging and contribution
contracts, while AppServer owns the wire projection.

| File | Purpose |
|------|---------|
| [plugin-architecture.md](plugin-architecture.md) | Plugin manifest, path rules, loading, diagnostics, tool sources, lifecycle, and protocol boundaries. |
| [skill-2.0.md](skill-2.0.md) | Skill storage, resolver behavior, management semantics, run records, configuration, and trust model. |
| [lsp-plugin.md](lsp-plugin.md) | Plugin-bundled LSP contribution model, effective merge rules, loading, diagnostics, and migration notes. |

Related domains: [Protocols](../protocols/), [Clients](../clients/), [SDK](../sdk/).

