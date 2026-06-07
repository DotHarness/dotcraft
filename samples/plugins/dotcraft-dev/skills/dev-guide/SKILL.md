---
name: dotcraft-dev-guide
description: Development workflow and project-specific norms for DotCraft. Use when changing protocol behavior, or shipping user-facing features. Covers spec-first workflow, testing rules and bilingual docs.
---

# DotCraft Development Guide

Project-specific workflow and norms for DotCraft.
For generic C#/Rust/React style, follow each ecosystem's standard conventions.
For repo orientation, read `CLAUDE.md`.

## Development Workflow

### Spec-First

When modifying protocol designs or process flows defined in `specs/`, update the spec first, then implement.
If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Steps

1. **Plan**: Search the codebase for similar features before adding new abstractions.
2. **Implement**: Follow the project-specific norms below. Add XML docs for public C# APIs. Update user-facing docs in both languages when behavior changes.
3. **Test**: Follow the testing rules below.
4. **Verify**: Confirm changes conform to the relevant spec and docs/examples are in place.

### Tool Schemas And Prompt Cache

When adding or changing model-visible tools, account for prompt cache stability. Avoid changing the tool schema solely because the thread switches operational modes, such as Plan to Agent. Prefer keeping the model-visible tool surface stable and enforcing mode-specific behavior with execution policy, runtime scopes, and prompt guidance.

Mode-specific tool removal is appropriate only when the mode represents a genuinely different role or runtime surface. For ordinary operational constraints, use `ModeToolPolicy` or an equivalent policy guard to reject disallowed calls, and make the current allowed tool usage clear in the system prompt or runtime context.

### Testing Rules

Tests are required for:

- Protocol-dependent code (JSON-RPC, wire format, session/appserver protocol): add conformance tests aligned with the spec.
- Complex multi-step flows or state machines: cover key paths and edge cases.

Tests are not required for:

- Small bug fixes that do not change an observable contract.
- Pure UI polish (layout, styling, copy tweaks) in desktop or TUI.
- Trivial refactors with no behavior change.

A test is meaningful only when (applies to C# xUnit and TypeScript tests equally):

- It catches a real regression, not language semantics, trivial getters/setters, or framework internals.
- It is not redundant with existing coverage; check before adding.
- It does not merely restate implementation details via excessive mocking; prefer state/output assertions unless interaction itself is the contract.
- It is not written just to inflate coverage numbers.

Desktop frontend tests:

- Test behavior, not static styling. Cover user-visible output, accessibility names,
  enabled/disabled state, navigation, store updates, IPC calls, serialization, and
  data mapping.
- Do not assert one-to-one visual implementation details such as CSS variables,
  colors, borders, shadows, spacing, radii, widths/heights, token values, source
  string snippets, or class names whose only purpose is styling.
- Keep style-related assertions only when the style is the functional contract,
  such as computed positions from a drag/drop or popover placement algorithm,
  ANSI color parsing, virtualized geometry, or resize behavior.
- Prefer short interaction tests over source/CSS guards. If a UI change is pure
  polish, document it or verify it manually instead of adding test volume.

DotCraft.Core C# xUnit tests:

- Test observable behavior through public APIs, persisted state, wire payloads, or user-visible results; do not test private implementation shape.
- When using TDD, work in vertical slices: write one behavior test, make the smallest useful implementation pass, then repeat.
- For protocol, session, and persistence behavior, prefer real temp stores/services and small fakes over mocks; use interaction assertions only when the interaction is the contract.
- Do not add tests for trivial formatters, getters, record equality, text passthrough, description/prompt wording, framework behavior, or coverage inflation.
- Use theories to consolidate repetitive variants only when each row protects a meaningful externally visible case.

Pre-commit: run the relevant full suites for touched areas (`dotnet test` for C#, and corresponding `npm test`/`cargo test` where applicable). Do not commit with known failures.

### Language Preference

- **Code comments**: English
- **Desktop UI strings**: Desktop owns UI localization. Localize for all supported app locales — `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, `de` (the source of truth is `desktop/src/shared/locales/types.ts`). This applies to message catalogs and to data-driven UI text such as plugin/extension descriptor `localizedLabel`; provide every supported locale, missing ones fall back to the base string.
- **C# runtime/UI-adjacent messages**: Do not add UI localization state or server-side translation catalogs. C# should emit stable machine-readable keys/codes plus English fallback text (`FallbackText` for CLI/server fallback copy). Desktop owns UI localization.
- **Protocol-visible system messages**: New client-visible notifications and errors must provide a stable key/code, structured params where useful, and an English fallback. User text, model output, and raw tool output must pass through unchanged.
- **Docs vs UI**: Documentation (`docs/`) is bilingual (English + Chinese) only; UI strings cover all supported locales above.

## Documentation Guidelines

DotCraft documentation lives under `docs/` as a VitePress documentation site.
For the full documentation writing standard — audience/purpose routing, per-archetype page templates, voice rules, and the bilingual style guide — use the `dotcraft-docs-guide` skill. The essentials:

When creating or updating documentation:

- Provide all documentation in both Chinese and English.
- Consider whether the documentation location fits the existing site structure. When inserting new documentation, ask the user where it should go unless the user has already approved a location.
- Keep documentation concise and current. Do not include historical explanations, such as old-version migration rationale or why legacy behavior existed.
- Keep the style user-friendly. Avoid excessive code references unless the document is explicitly providing code examples, such as SDK documentation.
