---
name: dotcraft-dev-guide
description: DotCraft development conventions. Use when changing protocols or shipping user-facing functionality.
---

# DotCraft Development Guide

Project-specific workflow and norms for DotCraft.
For general language and framework style, follow the relevant ecosystem conventions.
For repo orientation, read the applicable `AGENTS.md` files.

## Development Workflow

### Spec-First

When modifying protocol designs or process flows defined in `specs/`, update the spec first, then implement.
If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Steps

1. **Plan**: Search the codebase for similar features before adding new abstractions.
2. **Implement**: Follow the project-specific norms below. Add XML docs for public C# APIs. Update affected user-facing docs and localized content when behavior changes.
3. **Test**: Follow the testing rules below.
4. **Verify**: Confirm changes conform to the relevant spec and docs/examples are in place.

### Tool Schemas And Prompt Cache

When adding or changing model-visible tools, account for prompt cache stability. Avoid changing the tool schema solely because the thread switches operational modes, such as Plan to Agent. Prefer keeping the model-visible tool surface stable and enforcing mode-specific behavior with execution policy, runtime scopes, and prompt guidance.

Mode-specific tool removal is appropriate only when the mode represents a genuinely different role or runtime surface. For ordinary operational constraints, use `ModeToolPolicy` or an equivalent policy guard to reject disallowed calls, and make the current allowed tool usage clear in the system prompt or runtime context.

### Testing Rules

Add tests when an observable contract changes, especially for protocol-dependent behavior and complex multi-step flows or state machines. Pure visual polish, copy changes, trivial refactors, and fixes that do not change an observable contract do not require new tests.

A meaningful test catches a real regression without duplicating existing coverage or testing language, framework, or private implementation details. Prefer assertions on public behavior, state, persisted data, wire payloads, and user-visible output. Use real temporary dependencies or small fakes instead of extensive mocking unless an interaction is itself the contract.

Tests must verify observable behavior, not repository layout or source/spec text; do not locate the repository root or assert that production files, directories, comments, prompts, or wording exist at fixed checkout paths. Filesystem assertions are appropriate only when file lifecycle is the behavior under test or the file is an explicit test fixture; otherwise use temporary fixtures and public APIs.

- **Frontend**: Test behavior such as accessibility, state, navigation, IPC, serialization, and data mapping. Do not assert styling details unless geometry or visual state is the functional contract; verify pure polish manually.
- **Core C#**: Test through public APIs and observable results. Skip trivial formatters, getters, record equality, text passthrough, prompt wording, and framework behavior.

### Language Preference

Before changing localized UI, inspect the repository's current locale configuration and catalogs. Treat those as the source of truth and update every currently supported locale; do not rely on a locale list embedded in this skill.

- **Code comments**: English
- **UI strings**: The client owns UI localization. Update every locale discovered from the current source of truth, including message catalogs and data-driven UI text such as localized plugin or extension labels.
- **C# runtime/UI-adjacent messages**: Do not add UI localization state or server-side translation catalogs. C# should emit stable machine-readable keys/codes plus English fallback text (`FallbackText` for CLI/server fallback copy). Desktop owns UI localization.
- **Protocol-visible system messages**: New client-visible notifications and errors must provide a stable key/code, structured params where useful, and an English fallback. User text, model output, and raw tool output must pass through unchanged.

## Documentation

Use `dotcraft-docs-guide` for all documentation and repository README work.

## References

- For SVG assets shipped with built-in skills, read `references/svg-style.md`.
