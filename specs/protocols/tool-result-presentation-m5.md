# Interactive Tool UI — M‑v: First Real App + SDK Ergonomics + Fallback

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Planned |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑v — First real app (Oratorio) + SDK ergonomics + non‑Desktop fallback |
| **Depends on** | M‑iii, M‑iv |

## 1. Overview

With the host complete (M‑ii–iv), prove the model end‑to‑end with a **real** App Binding app, make the app‑author experience ergonomic, and ensure non‑Desktop clients degrade gracefully. The `sdk/dotnet/samples/InteractiveToolSample` app is the minimal reference; Oratorio is the first production validator (parent [§15](tool-result-presentation.md)).

## 2. Goal

A real app presents interactive UIs for its tools (validating the whole stack); app authors can add an interactive UI with minimal boilerplate; TUI and channels render readable text for the same tool results.

## 3. Scope

- **SDK ergonomics**: a clean, documented way to (a) declare `_meta.ui` on catalog tools and (b) ship/serve `ui://` resources (e.g. bundle a folder of HTML/JS served by URI prefix, or register per‑URI). M‑i added the raw resource responder + models; M‑v makes authoring ergonomic.
- **Oratorio UIs** per parent [§15](tool-result-presentation.md): `ListBoardItems`→`ui://oratorio/board` (interactive board; "Open in Oratorio" via `ui/open-link`; refresh via loopback `fetch`), `GetBoardItem`→`ui://oratorio/item`, `QueueReviewRound`→`ui://oratorio/review` (queue via `tools/call`, `externalWrite` → approval).
- **Non‑Desktop fallback**: TUI and channels render `structuredResult` / `contentItems` as text (parent [§12](tool-result-presentation.md)); every UI‑bearing tool MUST also return a model‑ and human‑usable text result.

## 4. Non‑goals

- New bridge methods or host features → M‑iii / M‑iv.
- Security/acceptance sign‑off → M‑vi.

## 5. Behavioral contract

- In Desktop, Oratorio's tools render their interactive cards and their actions work (open, refresh, queue‑with‑approval).
- In TUI and at least one channel, the same tool results render as readable text with no loss to the model's view.
- An app author can add an interactive UI to an existing tool with a small, documented set of steps (the sample app demonstrates the full path).

## 6. Required workflow / lifecycle

The app declares `_meta.ui` and serves `ui://` (parent [§4](tool-result-presentation.md)); returns `content` / `structuredResult` / `_meta` per the audience split ([§5](tool-result-presentation.md)); always provides a text fallback ([§12](tool-result-presentation.md)). Desktop renders the iframe; non‑Desktop clients render text.

## 7. Constraints & compatibility

- **Fallback is mandatory**: every UI‑bearing tool returns a usable non‑UI result, so non‑Desktop clients and the model still work.
- Oratorio's loopback backend serves its UI bundle and allows the iframe's opaque origin (CORS) for data path B.
- App UI bundles must satisfy the per‑resource CSP (no disallowed network/origins).

## 8. Acceptance checklist

- [ ] Oratorio board / item / review tools render interactive UIs in Desktop with working actions per §15.
- [ ] The same tools render readable text in TUI and at least one channel.
- [ ] An app can declare + serve an interactive UI via documented, minimal SDK steps (sample app is the reference).
- [ ] Every UI‑bearing tool returns a usable non‑UI fallback.
- [ ] Conformance/integration coverage for declare → serve → render → fallback.

## 9. Open questions

- SDK resource bundling shape: folder served by URI prefix vs per‑URI registration vs embedded resources.
- Whether Oratorio is the first validator, or a smaller internal app validates first.
- Channel fallback fidelity: how much of `structuredResult` to render as text.
