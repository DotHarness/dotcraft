# AGENTS.md

Quick reference for coding agents in this repository.
For code, protocol, or spec changes, use the repository's [dotcraft-dev-guide](desktop/resources/plugins/dotcraft-bundled/plugins/dotcraft/skills/dotcraft-dev-guide/SKILL.md), including its spec-first workflow.

## Project

DotCraft is a .NET 10 / C# Agent Harness.
It uses a modular architecture where multiple entry points (CLI, editors, bots, APIs, GitHub workflows) connect to one workspace and share sessions, memory, skills, and tools under `.craft/`.

## Working Agreement

- Complete the requested work and necessary verification within the authorized scope. Reuse prior decisions and authorization; ask only when missing information materially changes the outcome or an action exceeds that scope.
- Explicit user instructions take precedence over skill guidance. If a skill causes a pause or departure from the request, link to the exact file, quote the relevant instruction, and explain how it applies.
- For documentation and README work, use [/dotcraft-docs-guide](desktop/resources/plugins/dotcraft-bundled/plugins/dotcraft/skills/dotcraft-docs-guide/SKILL.md). User-facing pages explain purpose, usage, and outcomes; implementation details belong in developer references.
- Finish with a concise account of the result, validation performed, and any remaining blockers.

## Commits and Pull Requests

- Run `/please-cleanup` before committing by default, without waiting for a reminder. This check does not itself authorize committing, pushing, or publishing.
- Commit messages contain a single subject line, with no body or trailers.
- Follow [CONTRIBUTING.md](CONTRIBUTING.md) when creating or updating a pull request; it owns the title and body format.

## Build & Test

Prerequisite: .NET 10 SDK. Desktop and TypeScript work also requires Node.js; see [CONTRIBUTING.md](CONTRIBUTING.md).

- Build: `dotnet build dotcraft.sln`
- Package (Windows): `build.bat`
- Run: `dotnet run --project src/DotCraft.App/DotCraft.App.csproj`
- Project tests: `dotnet test <test-project.csproj>`
- Single test: `dotnet test <test-project.csproj> --filter "FullyQualifiedName~TestClassName.TestMethodName"`
- Full .NET suite, when the affected scope warrants it: `dotnet test dotcraft.sln`

Use the [development guide's validation entry points](desktop/resources/plugins/dotcraft-bundled/plugins/dotcraft/skills/dotcraft-dev-guide/SKILL.md#validation) for Desktop, SDK, and documentation changes. Complete the relevant checks; broaden or repeat them only for new changes, failures, or unresolved concerns.

## Architecture (Top-Level)

- **Assembly boundaries**: `DotCraft.Agents` is the provider-neutral Agent foundation; `DotCraft.Core`
  owns the product kernel; `DotCraft.Runtime` owns reusable DI and Generic Host lifecycle;
  `DotCraft.AppServer` projects Core through `DotCraft.Protocol`; and `DotCraft.App` is the official
  composition root. See `specs/architecture/runtime-module-boundaries.md`.
- **Modules**: compiled modules implement `IDotCraftModule` and are discovered by
  `DotCraft.Generators`. Functional facets include `IToolSourceModule`, `IChannelServiceModule`, and
  `ISessionChannelModule`. Host factories and `IModuleHostComposition` belong to `DotCraft.App`, not
  to the Core module facets.
- **Session Core**: defined in `specs/architecture/session-core.md` with `Thread -> Turn -> Item` model.
  `ISessionService` is the central API for thread lifecycle, input submission, and approvals.
  Used by CLI, ACP, Automations, and external channel adapters.
- **AppServer**: defined in `specs/protocols/appserver-protocol.md`.
  It is an optional JSON-RPC 2.0 boundary over stdio/WebSocket, projecting the host-owned
  `ISessionService` to out-of-process clients without creating a second session kernel.
  Used by Desktop, CLI, ACP, and external channel adapters.
- **Config**: the official `DotCraft.App` host layers global `~/.craft/config.json` and workspace
  `.craft/config.json`. Modules define their own config sections via `[ConfigSection("Key")]` in
  each module assembly.

## Repo Map

- Agent foundation and kernel: `src/DotCraft.Agents/`, `src/DotCraft.Core/`
- Runtime, protocol boundary, and app: `src/DotCraft.Runtime/`, `src/DotCraft.AppServer/`,
  `src/DotCraft.Protocol/`, `src/DotCraft.App/`
- Source generators: `src/DotCraft.Generators/`
- Feature modules: `src/DotCraft.{Automations,...}/`
- TypeScript channel packages: `sdk/typescript/packages/channel-{qq,wecom,feishu,weixin,telegram}/`
- Specs and tests: `specs/`, `tests/`
- SDKs and clients: `sdk/`, `desktop/`
- Docs: `docs/` (English root, Chinese under `docs/zh/`)

## Localization

- **UI strings** (Desktop, incl. plugin/extension `localizedLabel` and message catalogs): update every supported app locale discovered from `desktop/src/shared/locales/types.ts` and the current catalogs.
- **Docs** (`docs/`): English root + Chinese under `docs/zh/` only.
- **C# runtime/protocol messages**: stable key/code + English fallback; Desktop owns UI localization (no server-side translation catalogs).

## Code Organization

- Prefer focused modules, services, components, and styles over growing central orchestration files.
- Target fewer than 500 lines for hand-written source files. Treat roughly 800 lines as a refactoring trigger, not a mechanical limit.
- When making a non-trivial change to a file over 800 lines, extract the responsibility being changed instead of adding another responsibility to that file. Keep the original file focused on composition, routing, or orchestration.
- Move related tests, types, local styles, and documentation with extracted code so ownership remains clear.
- Split large tests by behavior or scenario. Do not split files evenly by line count or introduce one-use wrappers solely to reduce file size.
- Apply this rule incrementally. A small, isolated fix may remain in a large file when extraction would broaden the change; state the reason in the handoff.
- Generated code, schemas, localization catalogs, snapshots, fixtures, and lock files are exempt. Change their source or generator instead of editing generated output by hand.

### Comments

- Omit comments unless they convey necessary information the code cannot express. Keep each comment to at most one or two sentences, including XML documentation.
- Explain non-obvious constants, constraints, or deliberate omissions; do not restate code or label blocks.
- Design rationale belongs in `specs/`, not at each call site.
- No tombstones: delete removed code and its pointers, not a note of what moved.
- A stale comment is worse than none; update or delete it with the code.

### Desktop styles

- Follow `specs/architecture/desktop-styles.md` for renderer CSS ownership and compatibility rules.
- Import the ordered global style graph once through `desktop/src/renderer/styles/index.css`; do not add declarations to that import-only manifest.
- Keep ordinary hand-written CSS below 500 formatted lines when practical. A file at or above 800 formatted lines must be split when receiving a non-trivial change. Import-only manifests, generated output, and third-party styles are exempt.
- Keep foundations, shared primitives, and feature styles separate. New global feature selectors require an ownership prefix; use CSS Modules for isolated leaf components when they do not need portal, ancestor-state, shared-class, or third-party DOM selectors.
- Use inline React styles only for values that genuinely depend on runtime data. Put static presentation in owned CSS.
- Do not mix a source-only style move with selector cleanup, formatting, cascade layers, or visual changes. Preserve effective rule order and verify production-mounted surfaces through the design system.

## Go Deeper

- Runtime boundaries: `specs/architecture/runtime-module-boundaries.md`
- Session and protocol specs: `specs/architecture/session-core.md`, `specs/protocols/appserver-protocol.md`, `specs/protocols/external-channel-adapter.md`
- Developer architecture overview: `docs/developing/architecture/overview.md`
- Desktop visual design: `specs/architecture/DESIGN.md` (read before changing Desktop colors, buttons, inputs, cards, modals, menus, or view styling)
- Client docs: `desktop/README.md`
