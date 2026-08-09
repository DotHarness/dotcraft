# AGENTS.md

Quick reference for coding agents in this repository.
For full development norms (code style, module rules, tool naming, bilingual docs), read `dev-guide` skill.

## Project

DotCraft is a .NET 10 / C# Agent Harness.
It uses a modular architecture where multiple entry points (CLI, editors, bots, APIs, GitHub workflows) connect to one workspace and share sessions, memory, skills, and tools under `.craft/`.

## Build & Test

Prerequisite: .NET 10 SDK (preview).

- Build: `dotnet build dotcraft.sln`
- Package (Windows): `build.bat`
- Package (Linux/macOS): `bash build_linux.bat`
- Run: `dotnet run --project src/DotCraft.App/DotCraft.App.csproj`
- Test: `dotnet test`
- Single test: `dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"`

## Architecture (Top-Level)

- **Modules**: all interaction modes are `IDotCraftModule`, discovered by the `DotCraft.Generators` source generators.
  Types: Host (standalone entry), Channel (managed by Gateway), Tool-only (tool providers only).
  Host priority: CLI=0. Gateway runs when no higher-priority Host is active.
- **Session Core**: defined in `specs/architecture/session-core.md` with `Thread -> Turn -> Item` model.
  `ISessionService` is the central API for thread lifecycle, input submission, and approvals.
  Used by CLI, ACP, Automations, and external channel adapters.
- **AppServer**: defined in `specs/protocols/appserver-protocol.md`.
  JSON-RPC 2.0 over stdio/WebSocket, projecting `ISessionService` to out-of-process clients.
  Used by Desktop, CLI, ACP, and external channel adapters (see `sdk/python/`).
- **Agents**: built on `Microsoft.Extensions.AI` in `DotCraft.Core.Agents`
  (agent factory/runner, tool injection/filtering, subagents, context compaction pipeline).
- **Config**: global `~/.craft/config.json` + workspace `.craft/config.json`.
  Modules define their own config sections via `[ConfigSection("Key")]` in each module assembly.

## Repo Map

- Core and app: `src/DotCraft.Core/`, `src/DotCraft.App/`, `src/DotCraft.Generators/`
- Feature modules: `src/DotCraft.{Unity,Automations,...}/`
- TypeScript channel packages: `sdk/typescript/packages/channel-{qq,wecom,feishu,weixin,telegram}/`
- Specs and tests: `specs/`, `tests/`
- SDKs and clients: `sdk/`, `desktop/`
- Docs: `docs/` (English root, Chinese under `docs/zh/`)

## Localization

- **UI strings** (Desktop, incl. plugin/extension `localizedLabel` and message catalogs): localize for all supported app locales — `en`, `zh-Hans`, `ja`, `ko`, `es`, `fr`, `de` (see `desktop/src/shared/locales/types.ts`).
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

### Desktop styles

- Follow `specs/architecture/desktop-styles.md` for renderer CSS ownership and compatibility rules.
- Import the ordered global style graph once through `desktop/src/renderer/styles/index.css`; do not add declarations to that import-only manifest.
- Keep ordinary hand-written CSS below 500 formatted lines when practical. A file at or above 800 formatted lines must be split when receiving a non-trivial change. Import-only manifests, generated output, and third-party styles are exempt.
- Keep foundations, shared primitives, and feature styles separate. New global feature selectors require an ownership prefix; use CSS Modules for isolated leaf components when they do not need portal, ancestor-state, shared-class, or third-party DOM selectors.
- Use inline React styles only for values that genuinely depend on runtime data. Put static presentation in owned CSS.
- Do not mix a source-only style move with selector cleanup, formatting, cascade layers, or visual changes. Preserve effective rule order and verify production-mounted surfaces through the design system.

## Go Deeper

- Development norms: `dev-guide` skill
- Large feature workflow: `feature-workflow` skill
- Protocol specs: `specs/architecture/session-core.md`, `specs/protocols/appserver-protocol.md`, `specs/protocols/external-channel-adapter.md`
- Desktop visual design: `specs/architecture/DESIGN.md` (read before changing Desktop colors, buttons, inputs, cards, modals, menus, or view styling)
- Client docs: `desktop/README.md`
