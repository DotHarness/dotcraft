---
name: dotcraft-dev-guide
description: Development conventions for the DotCraft repository. Use when changing its C#, TypeScript, protocol artifacts, or specs.
---

# DotCraft Development Guide

Project-specific workflow and norms for DotCraft.
For general language and framework style, follow the relevant ecosystem conventions.
For repo orientation, read the applicable `AGENTS.md` files.

## Development Workflow

### Spec-First

When modifying protocol designs or process flows defined in `specs/`, update the spec first, then implement.
If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Completion Criteria

- Reuse suitable codebase patterns before introducing new abstractions.
- Keep implementation, affected specifications, documentation, examples, and localized content aligned with the resulting behavior.
- Add XML documentation to public C# APIs only when it explains a necessary, non-obvious contract. Like other comments, keep each to at most one or two sentences.
- Complete the relevant validation below and report the results and any blockers concisely.

### Compatibility and Generated Contracts

When changing an external surface, check affected AppServer clients, CLI arguments, configuration, persisted sessions and resume behavior, and public SDK APIs. Account for supported consumers and stored data even when no caller appears in this repository.

For AppServer wire-contract changes, update the owning spec and C# contracts or RPC catalog, then run these commands from the repository root:

```bash
dotnet run --project tools/DotCraft.ProtocolGen -- generate
dotnet run --project tools/DotCraft.ProtocolGen -- check
```

Review source and generated artifacts together and include both in the same change. Follow `specs/sdk/protocol-contract-generation.md` for profiles and compatibility diffs; generated schemas and SDK bindings are not independent sources to edit.

### Tool Schemas And Prompt Cache

When adding or changing model-visible tools, account for prompt cache stability. Avoid changing the tool schema solely because the thread switches operational modes, such as Plan to Agent. Prefer keeping the model-visible tool surface stable and enforcing mode-specific behavior with execution policy, runtime scopes, and prompt guidance.

Mode-specific tool removal is appropriate only when the mode represents a genuinely different role or runtime surface. For ordinary operational constraints, use `ModeToolPolicy` or an equivalent policy guard to reject disallowed calls, and make the current allowed tool usage clear in the system prompt or runtime context.

### Testing Rules

Add tests when an observable contract changes, especially for protocol-dependent behavior and complex multi-step flows or state machines. Pure visual polish, copy changes, trivial refactors, and fixes that do not change an observable contract do not require new tests.

A meaningful test catches a real regression without duplicating existing coverage or testing language, framework, or private implementation details. Prefer assertions on public behavior, state, persisted data, wire payloads, and user-visible output. Use real temporary dependencies or small fakes instead of extensive mocking unless an interaction is itself the contract.

Tests must verify observable behavior, not repository layout or source/spec text; do not locate the repository root or assert that production files, directories, comments, prompts, or wording exist at fixed checkout paths. Filesystem assertions are appropriate only when file lifecycle is the behavior under test or the file is an explicit test fixture; otherwise use temporary fixtures and public APIs.

- **Frontend**: Test behavior such as accessibility, state, navigation, IPC, serialization, and data mapping. Do not assert styling details unless geometry or visual state is the functional contract; verify pure polish manually.
- **Core C#**: Test through public APIs and observable results. Skip trivial formatters, getters, record equality, text passthrough, prompt wording, and framework behavior.

### Validation

Select checks for the affected surface and its consumers. Complete required checks; broaden or repeat them only when new changes, failures, or unresolved concerns justify it.

| Surface | Entry points |
|---|---|
| .NET | From the repo root, build the affected project with `dotnet build <project.csproj>` and run `dotnet test <test-project.csproj>`, optionally with `--filter "FullyQualifiedName~TestClassName.TestMethodName"`. Use `dotnet test dotcraft.sln` when shared changes warrant the full suite. |
| Desktop | In `desktop/`, run `npm run typecheck` and relevant tests with `npm test -- <test-file>`. Run `npm run check:styles` for renderer style changes. |
| TypeScript SDK and packages | Use the owning package's `package.json` scripts for type checking and relevant tests. From `sdk/typescript/`, package scripts can be selected with `npm run <script> --workspace <package-name>`. |
| Documentation site | When a rendered site page changes, run `npm run build` in `docs/`. Apply the documentation guide's artifact-specific checks for other Markdown and README changes. |

For pure copy or visual changes, verify the affected content or presentation without adding tests that duplicate the implementation. Report any check that could not run and the reason.

### Language Preference

Before changing localized UI, inspect the repository's current locale configuration and catalogs. Treat those as the source of truth and update every currently supported locale; do not rely on a locale list embedded in this skill.

- **Code comments**: English
- **UI strings**: The client owns UI localization. Update every locale discovered from the current source of truth, including message catalogs and data-driven UI text such as localized plugin or extension labels.
- **C# runtime/UI-adjacent messages**: Do not add UI localization state or server-side translation catalogs. C# should emit stable machine-readable keys/codes plus English fallback text (`FallbackText` for CLI/server fallback copy). Desktop owns UI localization.
- **Protocol-visible system messages**: New client-visible notifications and errors must provide a stable key/code, structured params where useful, and an English fallback. User text, model output, and raw tool output must pass through unchanged.

## Documentation

Use [dotcraft-docs-guide](../dotcraft-docs-guide/SKILL.md) for all documentation and repository README work.

## References

- For SVG assets shipped with built-in skills, read `references/svg-style.md`.
