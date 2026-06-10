# Interactive Tool UI — M‑v: First Real App + SDK Ergonomics + Fallback

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | ✅ In‑repo delivered (decoupled mutate‑approval + `ServeStaticUiResources` + fallback test/docs); Oratorio = external live validation |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑v — Decoupled mutate‑approval + SDK ergonomics + fallback (Oratorio = external validation) |
| **Depends on** | M‑iii, M‑iv |

## 1. Overview

With the host complete (M‑ii–iv), prove the model end‑to‑end with a **real** App Binding app, make the app‑author experience ergonomic, and ensure non‑Desktop clients degrade gracefully. The `sdk/dotnet/samples/InteractiveToolSample` app is the minimal reference; Oratorio is the first production validator (parent [§15](tool-result-presentation.md)).

## 2. Goal

A real app presents interactive UIs for its tools (validating the whole stack); app authors can add an interactive UI with minimal boilerplate; TUI and channels render readable text for the same tool results.

> **Scope note (grounded).** Two facts reshape what M‑v builds **in this repo**:
> 1. **Oratorio is an external native app** — only its App Binding metadata (`apps.json`, plugin manifest under `desktop/resources/plugins/dotcraft-bundled/plugins/oratorio/`) lives here; its tools run in the external process. So Oratorio is the *external* validator; `sdk/dotnet/samples/InteractiveToolSample` is the in‑repo end‑to‑end vehicle.
> 2. **Non‑Desktop fallback already works** — the TUI (`tui/src/app/event_mapper.rs` `structured_invocation_result_text`) and the channel adapter (`src/DotCraft.App/CLI/Rendering/StreamAdapter.cs` `FormatStructuredInvocationResult`) already render `contentItems` / `structuredResult` / `errorMessage` as text and filter `_meta`. It needs a **conformance test + the documented "always return text" rule**, not new rendering code.
>
> **In‑repo M‑v deliverables:** (1) **decoupled mutate‑approval** for `ui/tool/call` (the M‑iii deferral); (2) **SDK ergonomics** — folder/prefix `ui://` static serving; (3) **fallback conformance test** + author docs. Oratorio = external validation (metadata + contract docs).

- **Decoupled mutate‑approval** (the M‑iii deferral, [m3 §9](tool-result-presentation-m3.md)): a UI‑initiated `ui/tool/call` on a tool with an approval descriptor (`mutate`/`externalWrite`) is no longer rejected — the host raises a **decoupled approval** and the `ui/tool/call` **awaits** it, then dispatches or rejects. It **reuses Desktop's existing approval surface** (`ApprovalDecisionComposer` + the accept/acceptForSession/acceptAlways/decline options) via a **transient `PendingApproval`** and the existing server→client approval request/response transport (`AppServerInteractiveRequestSender` + `sendServerResponse`). **Decoupling preserved:** no turn is created and **no persisted `approvalCard` conversation item** — the prompt is transient host UI keyed by `threadId + approvalId`; every decision is audited. `read`/no‑approval calls still proceed without prompting.
- **SDK ergonomics**: a **folder/prefix static `ui://` server** — e.g. `ServeStaticUiResources(uriPrefix, folderPath)` registers every file in a folder under a `ui://` prefix (so `ui://app/index.html`, `ui://app/app.js`, … serve from disk with the right MIME), replacing per‑URI `RegisterResourceHandler` + inline HTML. (CSP‑builder and loopback‑CORS helpers are **out of scope** for this milestone.)
- **Non‑Desktop fallback**: **already implemented** (TUI + channel render text, `_meta` filtered). M‑v adds a **conformance test** asserting the model‑/human‑visible text equals `contentItems`/`structuredResult` with `_meta` excluded, and documents the **mandatory text‑fallback rule** for app authors.
- **App deep‑links via `ui/open-link`** (the M‑iii deferral): the host scheme policy gains one **binding‑scoped** allowance — the bound app's declared `nativeApplication.protocol` (from its catalog descriptor, plus per‑platform overrides) is accepted alongside `https:`/`mailto:`. Powers "Open in Oratorio" (`oratorio://open/task/{id}`); undeclared schemes stay rejected; blocked opens stay audited.
- **Oratorio (external validation):** keep the `apps.json` metadata correct and document the [§15](tool-result-presentation.md) contract; the live board/item/review validation happens with the real external Oratorio app, out of this repo.

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

## 8. Acceptance checklist (in‑repo)

- [x] A UI‑initiated `ui/tool/call` on a mutating tool raises a **decoupled approval** (reusing `ApprovalDecisionComposer`); on accept it dispatches and returns the result to the UI, on decline it returns an error — with **no turn and no persisted conversation item**, and the decision audited. *(C# `InvokeUiToolAsync` gate + `BuildUiToolApprovalGate`; renderer `ui/tool/approval/request` → generic‑approval slot.)*
- [x] `read` / no‑approval UI tool calls still proceed without an approval prompt (M‑iii behavior unchanged).
- [x] An app can serve a **folder** of `ui://` resources via `ServeStaticUiResources(uriPrefix, folder)` (correct MIME per extension). *(The sample keeps its inline card for self‑containedness; the README documents the helper as the multi‑tool path.)*
- [x] A conformance test asserts non‑Desktop text fallback renders `contentItems` and excludes UI‑only `_meta` / `widgetState` / `ui`.
- [x] App‑author docs cover the mandatory text‑fallback rule and the folder‑serving helper.
- [x] `ui/open-link` accepts the bound app's declared `nativeApplication.protocol` deep‑link scheme (binding‑scoped); undeclared custom schemes remain rejected and audited.
- [x] Oratorio `apps.json` metadata matches the §15 contract — toolCatalog names/scopes and the `oratorio` deep‑link protocol (all platforms); the Oratorio repo now declares `_meta.ui` on the three board tools and serves `ui://oratorio/board|item|review.html` (live validation happens with the running app).

## 9. Resolved decisions

- **Decoupled mutate‑approval mechanism** → **reuse** Desktop's `ApprovalDecisionComposer` and its decision options (accept / acceptForSession / acceptAlways / decline / cancel). The decoupled `ui/tool/call` path raises the approval over the **existing** server→client approval request/response transport, surfaced as a **transient `PendingApproval`** (no persisted `approvalCard` item, no turn — decoupling intact), keyed by `threadId + approvalId`, audited. The tool's `Approval` descriptor (`Kind` ∈ file/shell/remoteResource) maps to the composer's `approvalType`; `operation`/`target` derive from the descriptor + call arguments.
- **SDK resource bundling shape** → a **folder/prefix static server** (`ServeStaticUiResources(uriPrefix, folder)`); not per‑URI registration or embedded resources. Inline/per‑URI handlers remain for advanced cases.
- **First validator** → **Oratorio validates externally** (it is an out‑of‑repo native app); the in‑repo `InteractiveToolSample` is the end‑to‑end vehicle and SDK reference. No separate smaller internal app is introduced.
- **Channel fallback fidelity** → **already implemented and kept** — render `contentItems` text + `structuredResult` as pretty JSON + `errorMessage`, with `_meta` excluded. M‑v pins this with a conformance test rather than changing fidelity.
